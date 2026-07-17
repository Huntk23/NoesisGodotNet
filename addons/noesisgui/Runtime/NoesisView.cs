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
    private Label _errorOverlay;

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

        // Zero-copy backend renders GPU-side (bottom-up); readback is upright.
        FlipV = _host.OutputIsFlipped;

        Resized += OnResized;
        // Activation drives focus visuals (caret blink, active selection).
        // Tied to Godot focus so exactly one view shows a caret at a time.
        FocusEntered += _host.Activate;
        FocusExited += _host.Deactivate;
        if (HasFocus())
        {
            _host.Activate();
        }

        _host.CursorChanged += shape => MouseDefaultCursorShape = shape;
        _host.ReloadFailed += ShowReloadError;
        _host.ReloadSucceeded += HideReloadError;
    }

    private void ShowReloadError(string message)
    {
        if (_errorOverlay == null)
        {
            _errorOverlay = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _errorOverlay.AddThemeColorOverride("font_color", new Color(1f, 0.62f, 0.62f));
            _errorOverlay.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0f, 0f, 0.88f),
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 8,
                ContentMarginBottom = 8,
            });
            AddChild(_errorOverlay);
            // Full-width strip hugging the bottom edge, growing UPWARD as the message wraps (plain BottomWide pins a zero-height rect at the
            // edge and the text renders below the view — invisible).
            _errorOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
            _errorOverlay.GrowVertical = GrowDirection.Begin;
        }
        _errorOverlay.Text = $"XAML error in '{Xaml}' (showing last good view):\n{message}";
        _errorOverlay.Visible = true;
    }

    private void HideReloadError()
    {
        if (_errorOverlay != null)
        {
            _errorOverlay.Visible = false;
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

    /// <summary>Gamepad navigation for the focused view (joypad events don't route through _GuiInput).</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!HasFocus() || !_host.IsValid || @event is not InputEventJoypadButton joy)
        {
            return;
        }

        Noesis.Key key = NoesisInputMapper.MapJoyButton(joy.ButtonIndex);
        if (key == Noesis.Key.None)
        {
            return;
        }

        bool handled = joy.Pressed ? _host.View.KeyDown(key) : _host.View.KeyUp(key);
        if (handled)
        {
            _redrawRequested = true;
            GetViewport().SetInputAsHandled();
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
