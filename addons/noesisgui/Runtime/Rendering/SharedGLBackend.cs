using System;
using System.Runtime.InteropServices;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Zero-copy render backend for Godot's Compatibility (OpenGL) renderer.
///
/// How it works:
///  1. Godot's GL context is current on the main thread under Compatibility (single-threaded rendering). We create a second context and share
///     GL objects with it via wglShareLists.
///  2. A blank Godot ImageTexture is created; RenderingServer exposes the GL texture id behind it (TextureGetNativeHandle).
///  3. In our context, that texture is attached to an FBO (plus a stencil renderbuffer Noesis needs) and Noesis renders straight into it.
///  4. Godot draws its own texture — no glReadPixels, no CPU upload.
///
/// The output is bottom-up (GL render-target convention), so OutputIsFlipped is true, and the displaying node compensates (FlipV / UV scale).
///
/// Unsupported configurations (Forward+, threaded Compatibility rendering) throw from Init; NoesisViewHost falls back to OffscreenGLBackend.
/// </summary>
internal sealed class SharedGLBackend : INoesisRenderBackend
{
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _hdc = IntPtr.Zero;
    private IntPtr _hglrc = IntPtr.Zero;
    private IntPtr _savedDc = IntPtr.Zero;
    private IntPtr _savedRc = IntPtr.Zero;

    private Noesis.RenderDeviceGL _device;
    private ImageTexture _texture;
    private uint _fbo;
    private uint _stencilRbo;
    private int _width;
    private int _height;

    // GL extension entry points (loaded with our context current)
    private GenFramebuffersProc _glGenFramebuffers;
    private DeleteFramebuffersProc _glDeleteFramebuffers;
    private BindFramebufferProc _glBindFramebuffer;
    private FramebufferTexture2DProc _glFramebufferTexture2D;
    private GenRenderbuffersProc _glGenRenderbuffers;
    private DeleteRenderbuffersProc _glDeleteRenderbuffers;
    private BindRenderbufferProc _glBindRenderbuffer;
    private RenderbufferStorageProc _glRenderbufferStorage;
    private FramebufferRenderbufferProc _glFramebufferRenderbuffer;
    private CheckFramebufferStatusProc _glCheckFramebufferStatus;

    public Noesis.RenderDevice Device => _device;
    public bool OutputIsFlipped => true;

    /// <summary>Inexpensive pre-check: a GL context current on this thread means Compatibility renderer with single-threaded rendering.
    /// Windows-only (WGL); the OS guard also prevents a DllNotFoundException on other platforms.</summary>
    public static bool IsSupported() =>
        OperatingSystem.IsWindows() && wglGetCurrentContext() != IntPtr.Zero;

    public void Init(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        IntPtr godotRc = wglGetCurrentContext();
        if (godotRc == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No GL context current (Forward+/Mobile renderer, or threaded Compatibility rendering).");
        }

        // Hidden window purely to own a DC for our context.
        _hwnd = CreateWindowExW(0, "STATIC", "NoesisSharedGL", WS_POPUP,
            0, 0, 4, 4, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed.");
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
            throw new InvalidOperationException("SetPixelFormat failed.");
        }

        _hglrc = wglCreateContext(_hdc);
        if (_hglrc == IntPtr.Zero)
        {
            throw new InvalidOperationException("wglCreateContext failed.");
        }

        // Share textures/buffers with Godot's context BEFORE ours owns objects.
        if (!wglShareLists(godotRc, _hglrc))
        {
            throw new InvalidOperationException("wglShareLists with Godot's GL context failed.");
        }

        BeginContext();
        try
        {
            LoadGLFunctions();
            _device = new Noesis.RenderDeviceGL();
            CreateTarget();
        }
        finally
        {
            EndContext();
        }

        GD.Print($"[NoesisGUI] Zero-copy GL backend ready ({_width}x{_height}).");
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
            renderer.RenderOffscreen(); // Noesis binds its own FBOs here

            _glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
            glViewport(0, 0, _width, _height);
            glDisable(GL_SCISSOR_TEST);
            glColorMask(true, true, true, true);
            glClearColor(0f, 0f, 0f, 0f);
            glClearStencil(0);
            glClear(GL_COLOR_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);

            renderer.Render();

            // Cross-context visibility: Godot's context samples this texture later in the same thread's frame; a flush orders the commands.
            glFlush();
        }
        finally
        {
            EndContext();
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

        BeginContext();
        try
        {
            DestroyTarget();
            CreateTarget();
        }
        finally
        {
            EndContext();
        }
    }

    public void BeginContext()
    {
        _savedDc = wglGetCurrentDC();
        _savedRc = wglGetCurrentContext();
        if (!wglMakeCurrent(_hdc, _hglrc))
        {
            throw new InvalidOperationException("[NoesisGUI] wglMakeCurrent failed.");
        }
    }

    public void EndContext()
    {
        wglMakeCurrent(_savedDc, _savedRc);
        _savedDc = IntPtr.Zero;
        _savedRc = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hglrc != IntPtr.Zero)
        {
            BeginContext();
            try
            {
                DestroyTarget();
            }
            finally
            {
                EndContext();
            }

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
        _device = null;
        _texture = null;
    }

    private void CreateTarget()
    {
        // Godot-owned texture; its GL id is valid in our context via sharing.
        var blank = Image.CreateFromData(_width, _height, false, Image.Format.Rgba8,
            new byte[_width * _height * 4]);
        _texture = ImageTexture.CreateFromImage(blank);

        ulong glTextureId = RenderingServer.TextureGetNativeHandle(_texture.GetRid());
        if (glTextureId == 0)
        {
            throw new InvalidOperationException("TextureGetNativeHandle returned 0.");
        }

        uint[] ids = new uint[1];
        _glGenFramebuffers(1, ids);
        _fbo = ids[0];
        _glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        _glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, (uint)glTextureId, 0);

        _glGenRenderbuffers(1, ids);
        _stencilRbo = ids[0];
        _glBindRenderbuffer(GL_RENDERBUFFER, _stencilRbo);
        _glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH24_STENCIL8, _width, _height);
        _glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_RENDERBUFFER, _stencilRbo);

        uint status = _glCheckFramebufferStatus(GL_FRAMEBUFFER);
        if (status != GL_FRAMEBUFFER_COMPLETE)
        {
            throw new InvalidOperationException($"FBO incomplete (0x{status:X}).");
        }
        _glBindFramebuffer(GL_FRAMEBUFFER, 0);
    }

    private void DestroyTarget()
    {
        if (_fbo != 0)
        {
            _glDeleteFramebuffers(1, [_fbo]);
            _fbo = 0;
        }
        if (_stencilRbo != 0)
        {
            _glDeleteRenderbuffers(1, [_stencilRbo]);
            _stencilRbo = 0;
        }
        _texture = null; // Godot frees the GPU texture with the resource
    }

    private void LoadGLFunctions()
    {
        _glGenFramebuffers = GetProc<GenFramebuffersProc>("glGenFramebuffers");
        _glDeleteFramebuffers = GetProc<DeleteFramebuffersProc>("glDeleteFramebuffers");
        _glBindFramebuffer = GetProc<BindFramebufferProc>("glBindFramebuffer");
        _glFramebufferTexture2D = GetProc<FramebufferTexture2DProc>("glFramebufferTexture2D");
        _glGenRenderbuffers = GetProc<GenRenderbuffersProc>("glGenRenderbuffers");
        _glDeleteRenderbuffers = GetProc<DeleteRenderbuffersProc>("glDeleteRenderbuffers");
        _glBindRenderbuffer = GetProc<BindRenderbufferProc>("glBindRenderbuffer");
        _glRenderbufferStorage = GetProc<RenderbufferStorageProc>("glRenderbufferStorage");
        _glFramebufferRenderbuffer = GetProc<FramebufferRenderbufferProc>("glFramebufferRenderbuffer");
        _glCheckFramebufferStatus = GetProc<CheckFramebufferStatusProc>("glCheckFramebufferStatus");
    }

    private static T GetProc<T>(string name) where T : class
    {
        IntPtr addr = wglGetProcAddress(name);
        if (addr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Missing GL entry point '{name}'.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(addr);
    }

    private delegate void GenFramebuffersProc(int n, uint[] ids);
    private delegate void DeleteFramebuffersProc(int n, uint[] ids);
    private delegate void BindFramebufferProc(uint target, uint framebuffer);
    private delegate void FramebufferTexture2DProc(uint target, uint attachment, uint textarget, uint texture, int level);
    private delegate void GenRenderbuffersProc(int n, uint[] ids);
    private delegate void DeleteRenderbuffersProc(int n, uint[] ids);
    private delegate void BindRenderbufferProc(uint target, uint renderbuffer);
    private delegate void RenderbufferStorageProc(uint target, uint internalformat, int width, int height);
    private delegate void FramebufferRenderbufferProc(uint target, uint attachment, uint renderbuffertarget, uint renderbuffer);
    private delegate uint CheckFramebufferStatusProc(uint target);

    private const uint WS_POPUP = 0x80000000;
    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;
    private const uint PFD_DOUBLEBUFFER = 0x00000001;
    private const byte PFD_TYPE_RGBA = 0;

    private const uint GL_FRAMEBUFFER = 0x8D40;
    private const uint GL_RENDERBUFFER = 0x8D41;
    private const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    private const uint GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    private const uint GL_DEPTH24_STENCIL8 = 0x88F0;
    private const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    private const uint GL_STENCIL_BUFFER_BIT = 0x00000400;
    private const uint GL_SCISSOR_TEST = 0x0C11;

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

    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);

    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr hglrc);
    [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentDC();
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentContext();
    [DllImport("opengl32.dll")] private static extern bool wglShareLists(IntPtr hglrcSrc, IntPtr hglrcDst);
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32.dll")] private static extern void glDisable(uint cap);
    [DllImport("opengl32.dll")] private static extern void glColorMask(bool r, bool g, bool b, bool a);
    [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] private static extern void glClearStencil(int s);
    [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] private static extern void glFlush();
}
