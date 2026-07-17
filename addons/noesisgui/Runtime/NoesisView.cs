using Godot;

namespace NoesisGodot;

/// <summary>
/// A Godot Control hosting a NoesisGUI view (screen-space UI).
///
/// Pipeline (per frame): Noesis view Update → render on an offscreen GL context → readback into an ImageTexture displayed by
/// this TextureRect → Godot _GuiInput events forwarded to the Noesis view.
///
/// Works under BOTH Godot renderers (Forward+/Vulkan and Compatibility/GL) and composes like any Control. The heavy lifting lives in
/// NoesisViewHost (shared with NoesisView3D).
/// </summary>
[GlobalClass]
public partial class NoesisView : TextureRect
{
    /// <summary>XAML to load, relative to the provider root (see 'noesis_gui/resources/root'), e.g. "MainMenu.xaml".
    /// Absolute res:// paths are also accepted.</summary>
    [Export] public string Xaml { get; set; } = "";

    /// <summary>Continuous redraw. Disable to render only when RequestRedraw() is called.</summary>
    [Export] public bool AlwaysRender { get; set; } = true;

    private readonly NoesisViewHost _host = new();
    private bool _redrawRequested = true;

    /// <summary>WPF-style DataContext for the root element. Accepts any CLR object (INotifyPropertyChanged recommended).
    /// Can be set before or after _Ready.</summary>
    public object ViewModel
    {
        get => _host.ViewModel;
        set => _host.ViewModel = value;
    }

    /// <summary>The underlying Noesis view (input, timers). Null before _Ready.</summary>
    public Noesis.View View => _host.View;

    /// <summary>The loaded XAML root element. Null before _Ready.</summary>
    public Noesis.FrameworkElement Root => _host.Root;

    public override void _Ready()
    {
        // Godot composites this texture; Noesis outputs premultiplied alpha.
        Material = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha
        };
        StretchMode = StretchModeEnum.Scale;
        // Default KeepSize would make the frame texture dictate our minimum size, fighting container layout on every resize.
        ExpandMode = ExpandModeEnum.IgnoreSize;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;

        if (!_host.Init(Xaml, (Vector2I)Size, Name))
        {
            return;
        }

        Resized += OnResized;
        // Activation drives focus visuals (caret blink, active selection).
        // Tied to Godot focus so exactly one view shows a caret at a time.
        FocusEntered += _host.Activate;
        FocusExited += _host.Deactivate;
        if (HasFocus())
        {
            _host.Activate();
        }
    }

    public override void _Process(double delta)
    {
        if (!_host.IsValid)
        {
            return;
        }

        NoesisHotReload.Pump(); // no-op outside editor runs

        if (!AlwaysRender && !_redrawRequested)
        {
            return; // time is absolute (since _Ready), so animations stay coherent on resume
        }
        _redrawRequested = false;

        if (_host.RenderFrame())
        {
            Texture = _host.Texture; // cheap if unchanged
        }
    }

    /// <summary>Render one frame on the next _Process when AlwaysRender is off.</summary>
    public void RequestRedraw() => _redrawRequested = true;

    /// <summary>Reloads the XAML document and rebuilds the view, preserving the ViewModel.</summary>
    public void ReloadXaml()
    {
        _host.ReloadXaml();
        if (HasFocus())
        {
            _host.Activate();
        }
        _redrawRequested = true;
    }

    #region Input

    public override void _GuiInput(InputEvent @event)
    {
        if (!_host.IsValid)
        {
            return;
        }

        bool handled = NoesisInputMapper.Forward(_host.View, @event, this);
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
        _host.Resize((Vector2I)Size);
        _redrawRequested = true;
    }

    public override void _ExitTree()
    {
        _host.Dispose();
    }

    #endregion
}
