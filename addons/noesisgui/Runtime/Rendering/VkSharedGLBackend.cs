using System;
using System.Runtime.InteropServices;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Zero-copy backend for Godot's Forward+/Mobile (Vulkan) renderers on Windows.
///
/// Pipeline:
///  1. A VkImage with exportable memory is allocated on GODOT'S Vulkan device (VulkanInterop, handles via RenderingDevice.GetDriverResource).
///  2. A private WGL context imports that memory (GL_EXT_memory_object_win32) into a GL texture — same GPU allocation, no copies.
///  3. Noesis renders into it via FBO; glFinish makes the results visible to Vulkan (conservative sync; exported semaphores are a future refinement).
///  4. Godot samples the VkImage through TextureCreateFromExtension + Texture2DRD.
///
/// Requires Godot's Vulkan device to expose VK_KHR_external_memory_win32 and the GL driver to expose GL_EXT_memory_object_win32; init throws otherwise
/// and NoesisViewHost falls back to readback.
/// </summary>
internal sealed class VkSharedGLBackend : INoesisRenderBackend
{
    private RenderingDevice _rd;
    private IntPtr _vkDevice;
    private IntPtr _vkPhysicalDevice;

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _hdc = IntPtr.Zero;
    private IntPtr _hglrc = IntPtr.Zero;
    private IntPtr _savedDc = IntPtr.Zero;
    private IntPtr _savedRc = IntPtr.Zero;

    private Noesis.RenderDeviceGL _device;
    private VulkanInterop.ExportedImage _exported;
    private Rid _godotTextureRid;
    private Texture2Drd _texture;
    private uint _glMemoryObject;
    private uint _glTexture;
    private uint _fbo;
    private uint _stencilRbo;
    private int _width;
    private int _height;

    // GL extension entry points
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
    private CreateMemoryObjectsProc _glCreateMemoryObjectsEXT;
    private DeleteMemoryObjectsProc _glDeleteMemoryObjectsEXT;
    private ImportMemoryWin32HandleProc _glImportMemoryWin32HandleEXT;
    private TexStorageMem2DProc _glTexStorageMem2DEXT;

    public Noesis.RenderDevice Device => _device;
    public bool OutputIsFlipped => true;

    private static bool? _deviceSupportsExport;

    public static bool IsSupported()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        RenderingDevice rd = RenderingServer.GetRenderingDevice();
        if (rd == null)
        {
            return false; // Compatibility renderer
        }

        // Probe the export extension BEFORE any GL/Noesis objects exist: a half-initialized attempt is wasted work and risks side effects
        // on the fallback backend created right after. Cached per process.
        if (_deviceSupportsExport == null)
        {
            try
            {
                IntPtr device = (IntPtr)(long)(ulong)rd.GetDriverResource(
                    RenderingDevice.DriverResource.LogicalDevice, default, 0);
                _deviceSupportsExport = VulkanInterop.SupportsWin32HandleExport(device);
                if (_deviceSupportsExport == false)
                {
                    GD.Print("[NoesisGUI] Vulkan zero-copy unavailable: Godot's device lacks " +
                             "VK_KHR_external_memory_win32; using readback under Forward+/Mobile.");
                }
            }
            catch
            {
                _deviceSupportsExport = false;
            }
        }
        return _deviceSupportsExport == true;
    }

    public void Init(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null)
        {
            throw new InvalidOperationException("No RenderingDevice (Compatibility renderer).");
        }

        // If these enum members don't exist in your Godot version, the older
        // names are DriverResource.VulkanDevice / VulkanPhysicalDevice.
        _vkDevice = (IntPtr)(long)_rd.GetDriverResource(
            RenderingDevice.DriverResource.LogicalDevice, default, 0);
        _vkPhysicalDevice = (IntPtr)(long)_rd.GetDriverResource(
            RenderingDevice.DriverResource.PhysicalDevice, default, 0);
        if (_vkDevice == IntPtr.Zero || _vkPhysicalDevice == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not obtain Vulkan device handles from Godot.");
        }

        CreateGLContext();

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

        GD.Print($"[NoesisGUI] Vulkan-interop GL backend ready ({_width}x{_height}).");
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

            _glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
            glViewport(0, 0, _width, _height);
            glDisable(GL_SCISSOR_TEST);
            glColorMask(true, true, true, true);
            glClearColor(0f, 0f, 0f, 0f);
            glClearStencil(0);
            glClear(GL_COLOR_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);

            renderer.Render();

            // Conservative cross-API sync: ensure GL work is complete before
            // Godot's Vulkan queue samples the image this frame.
            glFinish();
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
        _rd = null;
    }

    private void CreateTarget()
    {
        // 1. Vulkan image with exportable memory, on Godot's device.
        _exported = VulkanInterop.CreateExportedImage(_vkDevice, _vkPhysicalDevice, (uint)_width, (uint)_height);

        // 2. Import into GL as a texture backed by the same memory.
        uint[] ids = new uint[1];
        _glCreateMemoryObjectsEXT(1, ids);
        _glMemoryObject = ids[0];
        _glImportMemoryWin32HandleEXT(_glMemoryObject, _exported.AllocationSize,
            GL_HANDLE_TYPE_OPAQUE_WIN32_EXT, _exported.Win32Handle);

        glGenTextures(1, ids);
        _glTexture = ids[0];
        glBindTexture(GL_TEXTURE_2D, _glTexture);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_TILING_EXT, (int)GL_OPTIMAL_TILING_EXT);
        _glTexStorageMem2DEXT(GL_TEXTURE_2D, 1, GL_RGBA8, _width, _height, _glMemoryObject, 0);

        // 3. FBO with the imported texture + stencil for Noesis.
        _glGenFramebuffers(1, ids);
        _fbo = ids[0];
        _glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        _glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _glTexture, 0);

        _glGenRenderbuffers(1, ids);
        _stencilRbo = ids[0];
        _glBindRenderbuffer(GL_RENDERBUFFER, _stencilRbo);
        _glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH24_STENCIL8, _width, _height);
        _glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_RENDERBUFFER, _stencilRbo);

        uint status = _glCheckFramebufferStatus(GL_FRAMEBUFFER);
        if (status != GL_FRAMEBUFFER_COMPLETE)
        {
            throw new InvalidOperationException($"Interop FBO incomplete (0x{status:X}).");
        }
        _glBindFramebuffer(GL_FRAMEBUFFER, 0);

        // 4. Hand the VkImage to Godot as a sampleable texture.
        _godotTextureRid = _rd.TextureCreateFromExtension(
            RenderingDevice.TextureType.Type2D,
            RenderingDevice.DataFormat.R8G8B8A8Unorm,
            RenderingDevice.TextureSamples.Samples1,
            RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
            _exported.Image, (ulong)_width, (ulong)_height, 1, 1);
        _texture = new Texture2Drd { TextureRdRid = _godotTextureRid };
    }

    private void DestroyTarget()
    {
        if (_godotTextureRid.IsValid)
        {
            _rd.FreeRid(_godotTextureRid);
            _godotTextureRid = default;
        }
        _texture = null;

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
        if (_glTexture != 0)
        {
            glDeleteTextures(1, [_glTexture]);
            _glTexture = 0;
        }
        if (_glMemoryObject != 0)
        {
            _glDeleteMemoryObjectsEXT(1, [_glMemoryObject]);
            _glMemoryObject = 0;
        }

        VulkanInterop.DestroyExportedImage(_vkDevice, in _exported);
        _exported = default;
    }

    private void CreateGLContext()
    {
        _hwnd = CreateWindowExW(0, "STATIC", "NoesisVkInterop", WS_POPUP,
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
        // I'm pretty sure these four only exist with GL_EXT_memory_object(_win32) support.
        _glCreateMemoryObjectsEXT = GetProc<CreateMemoryObjectsProc>("glCreateMemoryObjectsEXT");
        _glDeleteMemoryObjectsEXT = GetProc<DeleteMemoryObjectsProc>("glDeleteMemoryObjectsEXT");
        _glImportMemoryWin32HandleEXT = GetProc<ImportMemoryWin32HandleProc>("glImportMemoryWin32HandleEXT");
        _glTexStorageMem2DEXT = GetProc<TexStorageMem2DProc>("glTexStorageMem2DEXT");
    }

    private static T GetProc<T>(string name) where T : class
    {
        IntPtr addr = wglGetProcAddress(name);
        if (addr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Missing GL entry point '{name}' (driver lacks the extension).");
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
    private delegate void CreateMemoryObjectsProc(int n, uint[] memoryObjects);
    private delegate void DeleteMemoryObjectsProc(int n, uint[] memoryObjects);
    private delegate void ImportMemoryWin32HandleProc(uint memory, ulong size, uint handleType, IntPtr handle);
    private delegate void TexStorageMem2DProc(uint target, int levels, uint internalFormat, int width, int height, uint memory, ulong offset);

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
    private const uint GL_RGBA8 = 0x8058;
    private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    private const uint GL_STENCIL_BUFFER_BIT = 0x00000400;
    private const uint GL_SCISSOR_TEST = 0x0C11;
    private const uint GL_TEXTURE_TILING_EXT = 0x9580;
    private const uint GL_OPTIMAL_TILING_EXT = 0x9584;
    private const uint GL_HANDLE_TYPE_OPAQUE_WIN32_EXT = 0x9587;

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
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32.dll")] private static extern void glDisable(uint cap);
    [DllImport("opengl32.dll")] private static extern void glColorMask(bool r, bool g, bool b, bool a);
    [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] private static extern void glClearStencil(int s);
    [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] private static extern void glFinish();
    [DllImport("opengl32.dll")] private static extern void glGenTextures(int n, uint[] textures);
    [DllImport("opengl32.dll")] private static extern void glDeleteTextures(int n, uint[] textures);
    [DllImport("opengl32.dll")] private static extern void glBindTexture(uint target, uint texture);
    [DllImport("opengl32.dll")] private static extern void glTexParameteri(uint target, uint pname, int param);
}
