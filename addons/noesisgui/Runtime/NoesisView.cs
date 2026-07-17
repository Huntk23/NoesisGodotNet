using System;
using Godot;

namespace NoesisGodot;

/// <summary>
/// A Godot Control that hosts a NoesisGUI view.
///
/// Pipeline (per frame):
///   1. Noesis view Update() with elapsed time (animations, layout)
///   2. Render on an offscreen GL context (OffscreenGLBackend)
///   3. Readback into an ImageTexture displayed by this TextureRect
///   4. Godot input events are translated and forwarded to the Noesis view
///
/// The texture approach works under BOTH Godot renderers (Forward+/Vulkan and Compatibility/GL) and composes like any Control
/// (can be layered, shaded, placed in 3D via SubViewport, etc.). The cost is a GPU→CPU copy per frame.
/// </summary>
[GlobalClass]
public partial class NoesisView : TextureRect
{
    /// <summary>XAML to load, relative to the provider root (see 'noesis_gui/resources/root'), e.g. "MainMenu.xaml".
    /// Absolute res:// paths are also accepted.</summary>
    [Export] public string Xaml { get; set; } = "";

    /// <summary>Continuous redraw. Disable to render only when RequestRedraw() is called.</summary>
    [Export] public bool AlwaysRender { get; set; } = true;

    private Noesis.View _view;
    private Noesis.FrameworkElement _root;
    private OffscreenGLBackend _backend;
    private ImageTexture _texture;
    private ulong _startTicksMs;
    private bool _redrawRequested = true;
    private object _pendingViewModel;

    /// <summary>WPF-style DataContext for the root element. Accepts any CLR object (INotifyPropertyChanged recommended).
    /// Can be set before or after _Ready.</summary>
    public object ViewModel
    {
        get => _root?.DataContext ?? _pendingViewModel;
        set
        {
            _pendingViewModel = value;
            if (_root != null)
            {
                _root.DataContext = value;
            }
        }
    }

    /// <summary>The underlying Noesis view (input, timers). Null before _Ready.</summary>
    public Noesis.View View => _view;

    /// <summary>The loaded XAML root element. Null before _Ready.</summary>
    public Noesis.FrameworkElement Root => _root;

    public override void _Ready()
    {
        // Godot composites this texture; Noesis outputs premultiplied alpha.
        var material = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha
        };
        Material = material;
        StretchMode = StretchModeEnum.Scale;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;

        NoesisServer.EnsureInitialized();

        if (string.IsNullOrEmpty(Xaml))
        {
            GD.PushError($"[NoesisGUI] {Name}: 'Xaml' property is empty.");
            return;
        }

        try
        {
            _root = (Noesis.FrameworkElement)Noesis.GUI.LoadXaml(Xaml);
        }
        catch (Exception e)
        {
            GD.PushError($"[NoesisGUI] Failed to load '{Xaml}': {e.Message}");
            return;
        }

        if (_pendingViewModel != null)
        {
            _root.DataContext = _pendingViewModel;
        }

        _view = Noesis.GUI.CreateView(_root);
        // Optional: _view.SetFlags(Noesis.RenderFlags.PPAA); // vector-edge AA (LCD subpixel text is NOT suitable here:
        // the surface has a transparent background.)

        var size = GetViewSize();
        _view.SetSize(size.X, size.Y);

        _backend = new OffscreenGLBackend();
        _backend.Init(size.X, size.Y);

        // Renderer must be initialized with our GL context current.
        _backend.BeginContext();
        try
        {
            _view.Renderer.Init(_backend.Device);
        }
        finally
        {
            _backend.EndContext();
        }

        _startTicksMs = Time.GetTicksMsec();
        Resized += OnResized;
        NoesisHotReload.Register(this);
    }

    public override void _Process(double delta)
    {
        if (_view == null || _backend == null)
        {
            return;
        }

        NoesisHotReload.Pump(); // no-op outside editor runs

        if (!AlwaysRender && !_redrawRequested)
        {
            return; // time is absolute (since _Ready), so animations stay coherent on resume
        }
        _redrawRequested = false;

        double t = (Time.GetTicksMsec() - _startTicksMs) / 1000.0;
        Image frame = _backend.RenderFrame(_view, t);
        if (frame == null)
        {
            return;
        }

        if (_texture == null || _texture.GetSize() != frame.GetSize())
        {
            _texture = ImageTexture.CreateFromImage(frame);
            Texture = _texture;
        }
        else
        {
            _texture.Update(frame);
        }
    }

    /// <summary>Render one frame on the next _Process when AlwaysRender is off.</summary>
    public void RequestRedraw() => _redrawRequested = true;

    /// <summary>Reloads the XAML document and rebuilds the view, preserving the ViewModel. Used by hot-reload;
    /// also callable manually.</summary>
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
            // Common during hot-reload: half-written file or invalid markup. Keep showing the last good view.
            if (!NoesisHotReload.Silenced)
            {
                GD.PushWarning($"[NoesisGUI] Reload of '{Xaml}' failed, keeping previous view: {e.Message}");
            }
            return;
        }

        _backend.BeginContext();
        try
        {
            _view?.Renderer.Shutdown();
        }
        finally
        {
            _backend.EndContext();
        }

        _root = newRoot;
        if (_pendingViewModel != null)
        {
            _root.DataContext = _pendingViewModel;
        }

        _view = Noesis.GUI.CreateView(_root);
        var size = GetViewSize();
        _view.SetSize(size.X, size.Y);

        _backend.BeginContext();
        try
        {
            _view.Renderer.Init(_backend.Device);
        }
        finally
        {
            _backend.EndContext();
        }

        _redrawRequested = true;
    }
    
    #region Input
    public override void _GuiInput(InputEvent @event)
    {
        if (_view == null)
        {
            return;
        }

        bool handled = NoesisInputMapper.Forward(_view, @event, this);
        if (handled)
        {
            _redrawRequested = true;
            AcceptEvent();
        }

        if (@event is InputEventMouseButton { Pressed: true })
        {
            GrabFocus(); // route subsequent keyboard input here
        }
    }
    #endregion

    #region Lifecycle

    private void OnResized()
    {
        if (_view == null || _backend == null)
        {
            return;
        }
        var size = GetViewSize();
        _view.SetSize(size.X, size.Y);
        _backend.Resize(size.X, size.Y);
        _redrawRequested = true;
    }

    private Vector2I GetViewSize()
    {
        var s = (Vector2I)Size;
        return new Vector2I(Mathf.Max(s.X, 1), Mathf.Max(s.Y, 1));
    }

    public override void _ExitTree()
    {
        NoesisHotReload.Unregister(this);
        if (_view != null && _backend != null)
        {
            _backend.BeginContext();
            try
            {
                _view.Renderer.Shutdown();
            }
            finally
            {
                _backend.EndContext();
            }
        }
        _backend?.Dispose();
        _backend = null;
        _view = null;
        _root = null;
    }

    #endregion
}
