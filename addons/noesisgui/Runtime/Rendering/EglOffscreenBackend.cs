using System;
using System.Runtime.InteropServices;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Linux readback backend: a headless EGL pbuffer context renders Noesis and glReadPixels feeds a Godot ImageTexture. Works under every
/// Godot renderer (Forward+/Mobile/Compatibility) on X11 and Wayland.
///
/// Context restores subtlety: Godot's Compatibility renderer may hold its GL context via GLX (X11 session) or EGL (Wayland). Making our
/// EGL context current displaces either, so Begin/EndContext saves and restores BOTH, whichever was active. GLX entry points may not exist
/// on pure-Wayland systems, so those calls are guarded.
/// </summary>
internal sealed class EglOffscreenBackend : INoesisRenderBackend
{
    private IntPtr _display = IntPtr.Zero;   // EGLDisplay (default display, never terminated)
    private IntPtr _config = IntPtr.Zero;
    private IntPtr _context = IntPtr.Zero;
    private IntPtr _surface = IntPtr.Zero;   // pbuffer

    // Saved caller context (either API)
    private IntPtr _savedEglDisplay, _savedEglDraw, _savedEglRead, _savedEglCtx;
    private IntPtr _savedGlxDisplay, _savedGlxDrawable, _savedGlxCtx;

    private Noesis.RenderDeviceGL _device;
    private int _width;
    private int _height;
    private byte[] _readback;
    private byte[] _flipped;
    private Image _image;
    private ImageTexture _texture;

    private BindFramebufferProc _glBindFramebuffer;

    public Noesis.RenderDevice Device => _device;
    public bool OutputIsFlipped => false; // rows flipped on CPU during readback

    public void Init(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        _display = eglGetDisplay(IntPtr.Zero /* EGL_DEFAULT_DISPLAY */);
        if (_display == IntPtr.Zero)
        {
            throw new InvalidOperationException("eglGetDisplay failed.");
        }
        if (!eglInitialize(_display, out _, out _))
        {
            throw new InvalidOperationException($"eglInitialize failed (0x{eglGetError():X}).");
        }
        if (!eglBindAPI(EGL_OPENGL_API))
        {
            throw new InvalidOperationException("eglBindAPI(EGL_OPENGL_API) failed — desktop GL unavailable.");
        }

        int[] configAttribs =
        [
            EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
            EGL_RENDERABLE_TYPE, EGL_OPENGL_BIT,
            EGL_RED_SIZE, 8,
            EGL_GREEN_SIZE, 8,
            EGL_BLUE_SIZE, 8,
            EGL_ALPHA_SIZE, 8,
            EGL_STENCIL_SIZE, 8,
            EGL_NONE
        ];
        var configs = new IntPtr[1];
        if (!eglChooseConfig(_display, configAttribs, configs, 1, out int numConfigs) || numConfigs < 1)
        {
            throw new InvalidOperationException("eglChooseConfig found no RGBA8+stencil pbuffer config.");
        }
        _config = configs[0];

        _context = eglCreateContext(_display, _config, IntPtr.Zero /* EGL_NO_CONTEXT */, null);
        if (_context == IntPtr.Zero)
        {
            throw new InvalidOperationException($"eglCreateContext failed (0x{eglGetError():X}).");
        }

        CreateSurface();

        BeginContext();
        try
        {
            IntPtr proc = eglGetProcAddress("glBindFramebuffer");
            if (proc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Missing GL entry point 'glBindFramebuffer'.");
            }
            _glBindFramebuffer = Marshal.GetDelegateForFunctionPointer<BindFramebufferProc>(proc);

            _device = new Noesis.RenderDeviceGL();
        }
        finally
        {
            EndContext();
        }

        AllocateBuffers();
        GD.Print($"[NoesisGUI] EGL offscreen backend ready ({_width}x{_height}).");
    }

    public Texture2D RenderFrame(Noesis.View view, double timeSeconds)
    {
        if (_device == null)
        {
            return null;
        }

        BeginContext();
        try
        {
            view.Update(timeSeconds);
            var renderer = view.Renderer;
            renderer.UpdateRenderTree();
            renderer.RenderOffscreen();

            _glBindFramebuffer(GL_FRAMEBUFFER, 0);
            glViewport(0, 0, _width, _height);
            glDisable(GL_SCISSOR_TEST);
            glColorMask(true, true, true, true);
            glClearColor(0f, 0f, 0f, 0f);
            glClearStencil(0);
            glClear(GL_COLOR_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);

            renderer.Render();

            glPixelStorei(GL_PACK_ALIGNMENT, 1);
            glReadPixels(0, 0, _width, _height, GL_RGBA, GL_UNSIGNED_BYTE, _readback);
        }
        finally
        {
            EndContext();
        }

        // GL rows are bottom-up; Godot images are top-down.
        int stride = _width * 4;
        for (int y = 0; y < _height; y++)
        {
            Buffer.BlockCopy(_readback, (_height - 1 - y) * stride, _flipped, y * stride, stride);
        }
        _image.SetData(_width, _height, false, Image.Format.Rgba8, _flipped);

        if (_texture == null || _texture.GetSize() != _image.GetSize())
        {
            _texture = ImageTexture.CreateFromImage(_image);
        }
        else
        {
            _texture.Update(_image);
        }
        return _texture;
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (width == _width && height == _height)
        {
            return;
        }
        _width = width;
        _height = height;

        DestroySurface();
        CreateSurface();
        AllocateBuffers();
    }

    public void BeginContext()
    {
        // Save whichever GL binding is current (EGL on Wayland, GLX on X11).
        _savedEglDisplay = eglGetCurrentDisplay();
        _savedEglCtx = eglGetCurrentContext();
        _savedEglDraw = eglGetCurrentSurface(EGL_DRAW);
        _savedEglRead = eglGetCurrentSurface(EGL_READ);

        _savedGlxCtx = IntPtr.Zero;
        try
        {
            _savedGlxCtx = glXGetCurrentContext();
            if (_savedGlxCtx != IntPtr.Zero)
            {
                _savedGlxDisplay = glXGetCurrentDisplay();
                _savedGlxDrawable = glXGetCurrentDrawable();
            }
        }
        catch (DllNotFoundException) { } // pure-Wayland system without GLX
        catch (EntryPointNotFoundException) { }

        if (!eglMakeCurrent(_display, _surface, _surface, _context))
        {
            throw new InvalidOperationException($"[NoesisGUI] eglMakeCurrent failed (0x{eglGetError():X}).");
        }
    }

    public void EndContext()
    {
        // Release ours, then restore whichever binding the caller had.
        eglMakeCurrent(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_savedEglCtx != IntPtr.Zero)
        {
            eglMakeCurrent(_savedEglDisplay, _savedEglDraw, _savedEglRead, _savedEglCtx);
        }
        else if (_savedGlxCtx != IntPtr.Zero)
        {
            try
            {
                glXMakeCurrent(_savedGlxDisplay, _savedGlxDrawable, _savedGlxCtx);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    public void Dispose()
    {
        DestroySurface();
        if (_context != IntPtr.Zero)
        {
            eglDestroyContext(_display, _context);
            _context = IntPtr.Zero;
        }
        // Deliberately NO eglTerminate: the display may be shared with Godot's own EGL use (Wayland); terminating it would take Godot down with us.
        _device = null;
        _texture = null;
    }

    private void CreateSurface()
    {
        int[] pbufferAttribs = [EGL_WIDTH, _width, EGL_HEIGHT, _height, EGL_NONE];
        _surface = eglCreatePbufferSurface(_display, _config, pbufferAttribs);
        if (_surface == IntPtr.Zero)
        {
            throw new InvalidOperationException($"eglCreatePbufferSurface failed (0x{eglGetError():X}).");
        }
    }

    private void DestroySurface()
    {
        if (_surface != IntPtr.Zero)
        {
            eglDestroySurface(_display, _surface);
            _surface = IntPtr.Zero;
        }
    }

    private void AllocateBuffers()
    {
        _readback = new byte[_width * _height * 4];
        _flipped = new byte[_width * _height * 4];
        _image = Image.CreateFromData(_width, _height, false, Image.Format.Rgba8, _flipped);
    }

    private delegate void BindFramebufferProc(uint target, uint framebuffer);

    private const int EGL_SURFACE_TYPE = 0x3033;
    private const int EGL_PBUFFER_BIT = 0x0001;
    private const int EGL_RENDERABLE_TYPE = 0x3040;
    private const int EGL_OPENGL_BIT = 0x0008;
    private const int EGL_ALPHA_SIZE = 0x3021;
    private const int EGL_BLUE_SIZE = 0x3022;
    private const int EGL_GREEN_SIZE = 0x3023;
    private const int EGL_RED_SIZE = 0x3024;
    private const int EGL_STENCIL_SIZE = 0x3026;
    private const int EGL_NONE = 0x3038;
    private const int EGL_WIDTH = 0x3057;
    private const int EGL_HEIGHT = 0x3056;
    private const uint EGL_OPENGL_API = 0x30A2;
    private const int EGL_DRAW = 0x3059;
    private const int EGL_READ = 0x305A;

    private const uint GL_FRAMEBUFFER = 0x8D40;
    private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    private const uint GL_STENCIL_BUFFER_BIT = 0x00000400;
    private const uint GL_SCISSOR_TEST = 0x0C11;
    private const uint GL_PACK_ALIGNMENT = 0x0D05;
    private const uint GL_RGBA = 0x1908;
    private const uint GL_UNSIGNED_BYTE = 0x1401;

    private const string EglLib = "libEGL.so.1";
    private const string GlLib = "libGL.so.1"; // glvnd: core GL + GLX entry points

    [DllImport(EglLib)] private static extern IntPtr eglGetDisplay(IntPtr nativeDisplay);
    [DllImport(EglLib)] private static extern bool eglInitialize(IntPtr display, out int major, out int minor);
    [DllImport(EglLib)] private static extern bool eglBindAPI(uint api);
    [DllImport(EglLib)] private static extern bool eglChooseConfig(IntPtr display, int[] attribs, IntPtr[] configs, int configSize, out int numConfig);
    [DllImport(EglLib)] private static extern IntPtr eglCreateContext(IntPtr display, IntPtr config, IntPtr shareContext, int[] attribs);
    [DllImport(EglLib)] private static extern bool eglDestroyContext(IntPtr display, IntPtr context);
    [DllImport(EglLib)] private static extern IntPtr eglCreatePbufferSurface(IntPtr display, IntPtr config, int[] attribs);
    [DllImport(EglLib)] private static extern bool eglDestroySurface(IntPtr display, IntPtr surface);
    [DllImport(EglLib)] private static extern bool eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);
    [DllImport(EglLib)] private static extern IntPtr eglGetCurrentContext();
    [DllImport(EglLib)] private static extern IntPtr eglGetCurrentDisplay();
    [DllImport(EglLib)] private static extern IntPtr eglGetCurrentSurface(int readDraw);
    [DllImport(EglLib)] private static extern IntPtr eglGetProcAddress(string name);
    [DllImport(EglLib)] private static extern int eglGetError();

    [DllImport(GlLib)] private static extern IntPtr glXGetCurrentContext();
    [DllImport(GlLib)] private static extern IntPtr glXGetCurrentDisplay();
    [DllImport(GlLib)] private static extern IntPtr glXGetCurrentDrawable();
    [DllImport(GlLib)] private static extern bool glXMakeCurrent(IntPtr display, IntPtr drawable, IntPtr context);

    [DllImport(GlLib)] private static extern void glViewport(int x, int y, int w, int h);
    [DllImport(GlLib)] private static extern void glDisable(uint cap);
    [DllImport(GlLib)] private static extern void glColorMask(bool r, bool g, bool b, bool a);
    [DllImport(GlLib)] private static extern void glClearColor(float r, float g, float b, float a);
    [DllImport(GlLib)] private static extern void glClearStencil(int s);
    [DllImport(GlLib)] private static extern void glClear(uint mask);
    [DllImport(GlLib)] private static extern void glPixelStorei(uint pname, int param);
    [DllImport(GlLib)] private static extern void glReadPixels(int x, int y, int w, int h, uint format, uint type, byte[] pixels);
}
