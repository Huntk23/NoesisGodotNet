using System;
using Godot;

namespace NoesisGodot;

/// <summary>
/// The node-agnostic core of a hosted Noesis view: XAML loading, view/renderer lifecycle, per-frame rendering into an ImageTexture,
/// hot-reload, disposal. NoesisView (2D Control) and NoesisView3D (world-space panel) both wrap this.
/// </summary>
public sealed class NoesisViewHost : IDisposable
{
    public string Xaml { get; private set; } = "";

    public Noesis.View View { get; private set; }

    public Noesis.FrameworkElement Root { get; private set; }

    /// <summary>The rendered frame. The instance is replaced on resize — re-read after RenderFrame().</summary>
    public Texture2D Texture { get; private set; }

    /// <summary>True when the backend output is bottom-up; the displaying node compensates with FlipV or a UV transform.</summary>
    public bool OutputIsFlipped => _backend?.OutputIsFlipped ?? false;

    public bool IsValid => View != null && _backend != null;

    private INoesisRenderBackend _backend;
    private Vector2I _size;
    private ulong _startTicksMs;
    private object _viewModel;
    private string _ownerName = "";
    private bool _isActive;

    /// <summary>WPF-style DataContext for the root element. Can be set before Init.</summary>
    public object ViewModel
    {
        get => Root?.DataContext ?? _viewModel;
        set
        {
            _viewModel = value;
            if (Root != null)
            {
                Root.DataContext = value;
            }
        }
    }

    /// <summary>Raised when a hot-reload attempt fails to parse (the last good view stays up).</summary>
    public event Action<string> ReloadFailed;

    /// <summary>Raised after a successful XAML hot-reload.</summary>
    public event Action ReloadSucceeded;

    /// <summary>Raised when the UI requests a mouse cursor (I-beam over text, hand over links).</summary>
    public event Action<Control.CursorShape> CursorChanged;

    internal void NotifyCursor(Control.CursorShape shape) => CursorChanged?.Invoke(shape);

    internal void NotifyReloadFailed(string message) => ReloadFailed?.Invoke(message);

    public bool Init(string xaml, Vector2I size, string ownerName)
    {
        _ownerName = ownerName;
        Xaml = xaml ?? "";
        _size = Clamp(size);

        NoesisServer.EnsureInitialized();

        if (string.IsNullOrEmpty(Xaml))
        {
            GD.PushError($"[NoesisGUI] {_ownerName}: 'Xaml' property is empty.");
            return false;
        }

        Noesis.FrameworkElement root;
        try
        {
            root = (Noesis.FrameworkElement) Noesis.GUI.LoadXaml(Xaml);
        }
        catch (Exception e)
        {
            GD.PushError($"[NoesisGUI] {_ownerName}: failed to load '{Xaml}': {e.Message}");
            return false;
        }

        if (_viewModel != null)
        {
            root.DataContext = _viewModel;
        }

        Noesis.View view = null;
        INoesisRenderBackend backend = null;
        bool rendererInitialized = false;
        try
        {
            view = Noesis.GUI.CreateView(root);
            view.SetSize(_size.X, _size.Y);
            backend = CreateBackend();
            InitializeRenderer(view, backend);
            rendererInitialized = true;
            if (_isActive)
            {
                view.Activate();
            }
        }
        catch (Exception e)
        {
            if (rendererInitialized)
            {
                TryShutdownRenderer(view, backend, "failed initialization cleanup");
            }

            TryDisposeBackend(backend, "failed initialization cleanup");
            GD.PushError($"[NoesisGUI] {_ownerName}: view initialization failed: {e.Message}");
            return false;
        }

        Root = root;
        View = view;
        _backend = backend;
        _startTicksMs = Time.GetTicksMsec();
        NoesisHotReload.Register(this);
        return true;
    }

    /// <summary>Picks the fastest backend the current platform + configuration supports.</summary>
    private INoesisRenderBackend CreateBackend()
    {
        bool wantZeroCopy = NoesisServer.GetSettingBool("noesis_gui/rendering/zero_copy", true);

        // Zero-copy under Compatibility (GL): shared context (Windows).
        if (wantZeroCopy && SharedGLBackend.IsSupported())
        {
            try
            {
                return InitializeBackend(new SharedGLBackend());
            }
            catch (Exception e)
            {
                GD.PushWarning($"[NoesisGUI] {_ownerName}: zero-copy GL init failed, " + $"falling back to readback: {e.Message}");
            }
        }

        // Zero-copy under Forward+/Mobile (Vulkan): external-memory interop (Windows).
        if (wantZeroCopy && VkSharedGLBackend.IsSupported())
        {
            try
            {
                return InitializeBackend(new VkSharedGLBackend());
            }
            catch (Exception e)
            {
                GD.PushWarning($"[NoesisGUI] {_ownerName}: Vulkan-interop init failed, " + $"falling back to readback: {e.Message}");
            }
        }

        // Readback: per-platform offscreen context.
        if (OperatingSystem.IsWindows())
        {
            return InitializeBackend(new OffscreenGLBackend());
        }

        if (OperatingSystem.IsLinux())
        {
            return InitializeBackend(new EglOffscreenBackend());
        }

        throw new PlatformNotSupportedException("NoesisGodotNet currently supports Windows and Linux (macOS is on the roadmap).");
    }

    /// <summary>Ticks and renders one frame into Texture. Returns false if not initialized.</summary>
    public bool RenderFrame()
    {
        if (!IsValid)
        {
            return false;
        }

        double t = (Time.GetTicksMsec() - _startTicksMs) / 1000.0;
        Texture2D frame = _backend.RenderFrame(View, t);
        if (frame == null)
        {
            return false;
        }

        Texture = frame;
        return true;
    }

    public void Resize(Vector2I size)
    {
        size = Clamp(size);
        if (!IsValid || size == _size)
        {
            return;
        }

        _size = size;
        View.SetSize(_size.X, _size.Y);
        _backend.Resize(_size.X, _size.Y);
    }

    /// <summary>Activation drives focus visuals (caret blink, active selection).</summary>
    public void Activate()
    {
        _isActive = true;
        View?.Activate();
    }

    public void Deactivate()
    {
        _isActive = false;
        View?.Deactivate();
    }

    /// <summary>Reloads the XAML and rebuilds the view, preserving the ViewModel.</summary>
    public void ReloadXaml()
    {
        if (_backend == null)
        {
            return;
        }

        Noesis.FrameworkElement newRoot = null;
        Noesis.View newView = null;
        bool rendererInitialized = false;
        try
        {
            newRoot = (Noesis.FrameworkElement) Noesis.GUI.LoadXaml(Xaml);
            if (_viewModel != null)
            {
                newRoot.DataContext = _viewModel;
            }

            newView = Noesis.GUI.CreateView(newRoot);
            newView.SetSize(_size.X, _size.Y);
            InitializeRenderer(newView, _backend);
            rendererInitialized = true;
            if (_isActive)
            {
                newView.Activate();
            }
        }
        catch (Exception e)
        {
            if (rendererInitialized)
            {
                TryShutdownRenderer(newView, _backend, "failed reload cleanup");
            }

            ReportReloadFailure(e);
            return;
        }

        Noesis.View previousView = View;
        Root = newRoot;
        View = newView;
        TryShutdownRenderer(previousView, _backend, "previous view shutdown after reload");
        ReloadSucceeded?.Invoke();
    }

    public void Dispose()
    {
        NoesisHotReload.Unregister(this);
        TryShutdownRenderer(View, _backend, "view disposal");
        TryDisposeBackend(_backend, "view disposal");
        _backend = null;
        View = null;
        Root = null;
        Texture = null;
        _isActive = false;
    }

    private T InitializeBackend<T>(T backend) where T : INoesisRenderBackend
    {
        try
        {
            backend.Init(_size.X, _size.Y);
            return backend;
        }
        catch
        {
            TryDisposeBackend(backend, "backend initialization cleanup");
            throw;
        }
    }

    private static void InitializeRenderer(Noesis.View view, INoesisRenderBackend backend)
    {
        // Renderer initialization must happen while the backend's GL context is current.
        backend.BeginContext();
        try
        {
            view.Renderer.Init(backend.Device);
        }
        finally
        {
            backend.EndContext();
        }
    }

    private static void ShutdownRenderer(Noesis.View view, INoesisRenderBackend backend)
    {
        backend.BeginContext();
        try
        {
            view.Renderer.Shutdown();
        }
        finally
        {
            backend.EndContext();
        }
    }

    private void TryShutdownRenderer(Noesis.View view, INoesisRenderBackend backend, string operation)
    {
        if (view == null || backend == null)
        {
            return;
        }

        try
        {
            ShutdownRenderer(view, backend);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[NoesisGUI] {_ownerName}: {operation} failed: {e.Message}");
        }
    }

    private void TryDisposeBackend(INoesisRenderBackend backend, string operation)
    {
        if (backend == null)
        {
            return;
        }

        try
        {
            backend.Dispose();
        }
        catch (Exception e)
        {
            GD.PushWarning($"[NoesisGUI] {_ownerName}: {operation} failed while disposing the render backend: {e.Message}");
        }
    }

    private void ReportReloadFailure(Exception e)
    {
        // Common during hot-reload: a half-written file, invalid markup, or renderer initialization failure.
        if (!NoesisHotReload.Silenced)
        {
            GD.PushWarning($"[NoesisGUI] {_ownerName}: reload of '{Xaml}' failed, keeping previous view: {e.Message}");
        }

        ReloadFailed?.Invoke(e.Message);
    }

    private static Vector2I Clamp(Vector2I s) => new(Mathf.Max(s.X, 1), Mathf.Max(s.Y, 1));
}
