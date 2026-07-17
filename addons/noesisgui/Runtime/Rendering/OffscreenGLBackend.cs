using System;
using System.Runtime.InteropServices;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Microsoft Windows MVP render backend.
///
/// Creates a hidden native window with its own WGL (OpenGL) context, hands the context to Noesis via RenderDeviceGL,
/// renders the view into the hidden window's backbuffer, and reads the pixels back into a Godot Image.
///
/// Why this design:
///  - Independent of Godot's renderer (works under Forward+/Vulkan AND Compatibility)
///  - Pure C#, no GDExtension/C++ toolchain
///  - Noesis GL state changes can never corrupt Godot's GL state (separate context)
///
/// Known cost: glReadPixels + texture upload per frame (fine for menus/HUDs at
/// 1080p).
///
/// Context-current pattern mirrors NoesisApp's RenderContextWGL (https://github.com/Noesis/Managed).
/// </summary>
public sealed class OffscreenGLBackend : INoesisRenderBackend
{
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _hdc = IntPtr.Zero;
    private IntPtr _hglrc = IntPtr.Zero;

    // Saved caller context for Begin/EndContext (critical under Compatibility renderer, where Godot's own GL context is current on this thread).
    private IntPtr _savedDc = IntPtr.Zero;
    private IntPtr _savedRc = IntPtr.Zero;

    private Noesis.RenderDeviceGL _device;
    private int _width;
    private int _height;

    private byte[] _readback;   // raw glReadPixels output (bottom-up)
    private byte[] _flipped;    // row-flipped RGBA for Godot
    private Image _image;
    private ImageTexture _texture;

    private BindFramebufferProc _glBindFramebuffer;

    public Noesis.RenderDevice Device => _device;

    /// <summary>Readback path flips rows on the CPU, so output is upright.</summary>
    public bool OutputIsFlipped => false;

    public void Init(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        // Hidden top-level window; "STATIC" is a predefined class, so no RegisterClass dance is needed. Never shown.
        _hwnd = CreateWindowExW(0, "STATIC", "NoesisOffscreen", WS_POPUP,
            0, 0, _width, _height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("[NoesisGUI] CreateWindowExW failed.");
        }

        _hdc = GetDC(_hwnd);

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = PFD_TYPE_RGBA,
            cColorBits = 32,
            cAlphaBits = 8,
            cStencilBits = 8,
        };
        int pf = ChoosePixelFormat(_hdc, ref pfd);
        if (pf == 0 || !SetPixelFormat(_hdc, pf, ref pfd))
        {
            throw new InvalidOperationException("[NoesisGUI] SetPixelFormat failed.");
        }

        _hglrc = wglCreateContext(_hdc);
        if (_hglrc == IntPtr.Zero)
        {
            throw new InvalidOperationException("[NoesisGUI] wglCreateContext failed.");
        }

        BeginContext();
        try
        {
            _glBindFramebuffer = GetGLProc<BindFramebufferProc>("glBindFramebuffer");
            _device = new Noesis.RenderDeviceGL();
        }
        finally
        {
            EndContext();
        }

        AllocateBuffers();
        GD.Print($"[NoesisGUI] Offscreen GL backend ready ({_width}x{_height}).");
    }

    public void Resize(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, _width, _height,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
        AllocateBuffers();
    }

    /// <summary>Ticks and renders the view, uploads the readback into an ImageTexture and returns it.</summary>
    public Texture2D RenderFrame(Noesis.View view, double timeSeconds)
    {
        Image frame = RenderImage(view, timeSeconds);
        if (frame == null)
        {
            return null;
        }

        if (_texture == null || _texture.GetSize() != frame.GetSize())
        {
            _texture = ImageTexture.CreateFromImage(frame);
        }
        else
        {
            _texture.Update(frame);
        }
        return _texture;
    }

    private Image RenderImage(Noesis.View view, double timeSeconds)
    {
        if (_device == null)
        {
            return null;
        }

        BeginContext();
        try
        {
            // Update phase (animations, layout, render tree)
            view.Update(timeSeconds);
            var renderer = view.Renderer;
            renderer.UpdateRenderTree();

            // Offscreen phase (opacity groups, effects) - Noesis binds its own FBOs
            renderer.RenderOffscreen();

            // Main pass into our hidden backbuffer
            _glBindFramebuffer(GL_FRAMEBUFFER, 0);
            glViewport(0, 0, _width, _height);
            glDisable(GL_SCISSOR_TEST);
            glColorMask(true, true, true, true);
            glClearColor(0f, 0f, 0f, 0f); // transparent background
            glClearStencil(0);
            glClear(GL_COLOR_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);

            renderer.Render();

            // Readback (synchronizes GL)
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
        return _image;
    }

    /// <summary>Makes the backend's GL context current, saving whatever context the caller had (Godot Compatibility renderer owns one on
    /// this thread!).</summary>
    public void BeginContext()
    {
        _savedDc = wglGetCurrentDC();
        _savedRc = wglGetCurrentContext();
        if (!wglMakeCurrent(_hdc, _hglrc))
        {
            throw new InvalidOperationException("[NoesisGUI] wglMakeCurrent failed.");
        }
    }

    /// <summary>Restores the caller's GL context (or none).</summary>
    public void EndContext()
    {
        wglMakeCurrent(_savedDc, _savedRc);
        _savedDc = IntPtr.Zero;
        _savedRc = IntPtr.Zero;
    }

    public void Dispose()
    {
        _device = null; // the owner must already shut down renderer

        if (_hglrc != IntPtr.Zero)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(_hglrc);
            _hglrc = IntPtr.Zero;
        }
        if (_hdc != IntPtr.Zero)
        {
            ReleaseDC(_hwnd, _hdc);
            _hdc = IntPtr.Zero;
        }
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void AllocateBuffers()
    {
        _readback = new byte[_width * _height * 4];
        _flipped = new byte[_width * _height * 4];
        _image = Image.CreateFromData(_width, _height, false, Image.Format.Rgba8, _flipped);
    }

    private static T GetGLProc<T>(string name) where T : class
    {
        IntPtr addr = wglGetProcAddress(name);
        if (addr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"[NoesisGUI] Missing GL entry point '{name}'.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(addr);
    }

    private delegate void BindFramebufferProc(uint target, uint framebuffer);

    private const uint WS_POPUP = 0x80000000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_HIDEWINDOW = 0x0080;

    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;
    private const uint PFD_DOUBLEBUFFER = 0x00000001;
    private const byte PFD_TYPE_RGBA = 0;

    private const uint GL_FRAMEBUFFER = 0x8D40;
    private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    private const uint GL_STENCIL_BUFFER_BIT = 0x00000400;
    private const uint GL_SCISSOR_TEST = 0x0C11;
    private const uint GL_PACK_ALIGNMENT = 0x0D05;
    private const uint GL_RGBA = 0x1908;
    private const uint GL_UNSIGNED_BYTE = 0x1401;

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte cAlphaBits, cAlphaShift;
        public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
        int x, int y, int w, int h, uint flags);

    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);

    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr hglrc);
    [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentDC();
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentContext();
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32.dll")] private static extern void glDisable(uint cap);
    [DllImport("opengl32.dll")] private static extern void glColorMask(bool r, bool g, bool b, bool a);
    [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] private static extern void glClearStencil(int s);
    [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] private static extern void glPixelStorei(uint pname, int param);
    [DllImport("opengl32.dll")] private static extern void glReadPixels(int x, int y, int w, int h,
        uint format, uint type, byte[] pixels);
}
