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
    public ImageTexture Texture { get; private set; }

    public bool IsValid => View != null && _backend != null;

    private OffscreenGLBackend _backend;
    private Vector2I _size;
    private ulong _startTicksMs;
    private object _viewModel;
    private string _ownerName = "";

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

        try
        {
            Root = (Noesis.FrameworkElement)Noesis.GUI.LoadXaml(Xaml);
        }
        catch (Exception e)
        {
            GD.PushError($"[NoesisGUI] {_ownerName}: failed to load '{Xaml}': {e.Message}");
            return false;
        }

        if (_viewModel != null)
        {
            Root.DataContext = _viewModel;
        }

        View = Noesis.GUI.CreateView(Root);
        View.SetSize(_size.X, _size.Y);

        _backend = new OffscreenGLBackend();
        _backend.Init(_size.X, _size.Y);

        // Renderer must be initialized with our GL context current.
        _backend.BeginContext();
        try
        {
            View.Renderer.Init(_backend.Device);
        }
        finally
        {
            _backend.EndContext();
        }

        _startTicksMs = Time.GetTicksMsec();
        NoesisHotReload.Register(this);
        return true;
    }

    /// <summary>Ticks and renders one frame into Texture. Returns false if not initialized.</summary>
    public bool RenderFrame()
    {
        if (!IsValid)
        {
            return false;
        }

        double t = (Time.GetTicksMsec() - _startTicksMs) / 1000.0;
        Image frame = _backend.RenderFrame(View, t);
        if (frame == null)
        {
            return false;
        }

        if (Texture == null || Texture.GetSize() != frame.GetSize())
        {
            Texture = ImageTexture.CreateFromImage(frame);
        }
        else
        {
            Texture.Update(frame);
        }
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
    public void Activate() => View?.Activate();

    public void Deactivate() => View?.Deactivate();

    /// <summary>Reloads the XAML and rebuilds the view, preserving the ViewModel.</summary>
    public void ReloadXaml()
    {
        if (_backend == null)
        {
            return;
        }

        Noesis.FrameworkElement newRoot;
        try
        {
            newRoot = (Noesis.FrameworkElement)Noesis.GUI.LoadXaml(Xaml);
        }
        catch (Exception e)
        {
            // Common during hot-reload: half-written file or invalid markup.
            if (!NoesisHotReload.Silenced)
            {
                GD.PushWarning($"[NoesisGUI] {_ownerName}: reload of '{Xaml}' failed, keeping previous view: {e.Message}");
            }
            return;
        }

        _backend.BeginContext();
        try
        {
            View?.Renderer.Shutdown();
        }
        finally
        {
            _backend.EndContext();
        }

        Root = newRoot;
        if (_viewModel != null)
        {
            Root.DataContext = _viewModel;
        }

        View = Noesis.GUI.CreateView(Root);
        View.SetSize(_size.X, _size.Y);

        _backend.BeginContext();
        try
        {
            View.Renderer.Init(_backend.Device);
        }
        finally
        {
            _backend.EndContext();
        }
    }

    public void Dispose()
    {
        NoesisHotReload.Unregister(this);
        if (View != null && _backend != null)
        {
            _backend.BeginContext();
            try
            {
                View.Renderer.Shutdown();
            }
            finally
            {
                _backend.EndContext();
            }
        }
        _backend?.Dispose();
        _backend = null;
        View = null;
        Root = null;
        Texture = null;
    }

    private static Vector2I Clamp(Vector2I s) => new(Mathf.Max(s.X, 1), Mathf.Max(s.Y, 1));
}
