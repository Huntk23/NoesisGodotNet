using Godot;

namespace NoesisGodot;

/// <summary>
/// A world-space NoesisGUI panel: XAML rendered onto a quad in 3D, with mouse input raycast from the camera and mapped into the view
/// (in-world screens, holo-panels, terminals).
///
/// The node is a StaticBody3D, so Godot's built-in ray picking delivers _InputEvent with world-space hit positions; it builds its own
/// QuadMesh and CollisionShape3D children at runtime.
///
/// Keyboard: the panel most recently clicked owns keyboard focus and receives key events via _UnhandledInput.
/// </summary>
[GlobalClass]
public partial class NoesisView3D : StaticBody3D
{
    /// <summary>XAML to load, relative to the provider root, e.g. "ThemeLab.xaml".</summary>
    [Export] public string Xaml { get; set; } = "";

    /// <summary>Panel size in world units (meters).</summary>
    [Export] public Vector2 PanelSize { get; set; } = new(1.6f, 0.9f);

    /// <summary>Texture resolution per world unit. 512 → a 1.6x0.9 panel renders at 819x460.</summary>
    [Export(PropertyHint.Range, "64,2048,1")] public int PixelsPerMeter { get; set; } = 512;

    /// <summary>Unshaded (UI-like, ignores lighting). Disable for lit surfaces.</summary>
    [Export] public bool Unshaded { get; set; } = true;

    /// <summary>Render the back face too.</summary>
    [Export] public bool DoubleSided { get; set; }

    private static NoesisView3D _keyboardOwner;

    private readonly NoesisViewHost _host = new();
    private MeshInstance3D _meshInstance;
    private StandardMaterial3D _material;
    private Vector2I _pixelSize;
    private bool _hovered;

    public object ViewModel
    {
        get => _host.ViewModel;
        set => _host.ViewModel = value;
    }

    public Noesis.View View => _host.View;
    public Noesis.FrameworkElement Root => _host.Root;

    public override void _Ready()
    {
        _pixelSize = new Vector2I(
            Mathf.Max((int)(PanelSize.X * PixelsPerMeter), 1),
            Mathf.Max((int)(PanelSize.Y * PixelsPerMeter), 1));

        if (!_host.Init(Xaml, _pixelSize, Name))
        {
            return;
        }

        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.PremultAlpha, // Noesis outputs premultiplied
            ShadingMode = Unshaded
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel,
            CullMode = DoubleSided
                ? BaseMaterial3D.CullModeEnum.Disabled
                : BaseMaterial3D.CullModeEnum.Back,
        };

        _meshInstance = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = PanelSize },
            MaterialOverride = _material,
        };
        AddChild(_meshInstance);

        var collision = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(PanelSize.X, PanelSize.Y, 0.01f) },
        };
        AddChild(collision);

        InputRayPickable = true;

        // Cursor forwarding, scoped to hover (a 3D panel doesn't own a screen region the way a Control does, so we set the global cursor and reset
        // it when the mouse leaves the collider).
        MouseEntered += () => _hovered = true;
        MouseExited += () =>
        {
            _hovered = false;
            Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        };
        _host.CursorChanged += shape =>
        {
            if (_hovered)
            {
                Input.SetDefaultCursorShape((Input.CursorShape)(int)shape);
            }
        };
    }

    public override void _Process(double delta)
    {
        if (!_host.IsValid)
        {
            return;
        }

        NoesisHotReload.Pump();

        if (_host.RenderFrame())
        {
            _material.AlbedoTexture = _host.Texture; // cheap if unchanged
        }
    }

    /// <summary>Mouse events delivered by Godot's ray picking (world-space hit position).</summary>
    public override void _InputEvent(Camera3D camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, int shapeIdx)
    {
        if (!_host.IsValid)
        {
            return;
        }

        Vector2I px = WorldToPixels(eventPosition);

        switch (@event)
        {
            case InputEventMouseMotion:
                _host.View.MouseMove(px.X, px.Y);
                break;

            case InputEventMouseButton button:
                switch (button.ButtonIndex)
                {
                    case MouseButton.WheelUp when button.Pressed:
                        _host.View.Scroll(px.X, px.Y, (int)(120 * (button.Factor > 0 ? button.Factor : 1f)));
                        break;
                    case MouseButton.WheelDown when button.Pressed:
                        _host.View.Scroll(px.X, px.Y, -(int)(120 * (button.Factor > 0 ? button.Factor : 1f)));
                        break;
                    case MouseButton.Left or MouseButton.Right or MouseButton.Middle:
                        Noesis.MouseButton nsButton = button.ButtonIndex switch
                        {
                            MouseButton.Right => Noesis.MouseButton.Right,
                            MouseButton.Middle => Noesis.MouseButton.Middle,
                            _ => Noesis.MouseButton.Left,
                        };
                        if (button.Pressed)
                        {
                            GrabKeyboard();
                            if (button.DoubleClick)
                            {
                                _host.View.MouseDoubleClick(px.X, px.Y, nsButton);
                            }
                            else
                            {
                                _host.View.MouseButtonDown(px.X, px.Y, nsButton);
                            }
                        }
                        else
                        {
                            _host.View.MouseButtonUp(px.X, px.Y, nsButton);
                        }
                        break;
                }
                break;
        }
    }

    /// <summary>Keyboard and gamepad for the focused panel (neither is position-based, so they don't come through ray picking).</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_keyboardOwner != this || !_host.IsValid)
        {
            return;
        }

        bool handled = false;

        switch (@event)
        {
            case InputEventKey key:
            {
                Noesis.Key nsKey = NoesisInputMapper.MapKey(key.Keycode);
                if (key.Pressed)
                {
                    if (nsKey != Noesis.Key.None)
                    {
                        handled |= _host.View.KeyDown(nsKey);
                    }
                    if (key.Unicode != 0)
                    {
                        handled |= _host.View.Char((uint)key.Unicode);
                    }
                }
                else if (nsKey != Noesis.Key.None)
                {
                    _host.View.KeyUp(nsKey);
                    handled = true;
                }
                break;
            }

            case InputEventJoypadButton joy:
            {
                Noesis.Key nsKey = NoesisInputMapper.MapJoyButton(joy.ButtonIndex);
                if (nsKey != Noesis.Key.None)
                {
                    handled = joy.Pressed ? _host.View.KeyDown(nsKey) : _host.View.KeyUp(nsKey);
                }
                break;
            }
        }

        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void GrabKeyboard()
    {
        if (_keyboardOwner == this)
        {
            return;
        }
        if (GodotObject.IsInstanceValid(_keyboardOwner))
        {
            _keyboardOwner._host.Deactivate();
        }
        _keyboardOwner = this;
        _host.Activate();
    }

    private Vector2I WorldToPixels(Vector3 worldPosition)
    {
        // QuadMesh is centered at the origin in our local space, facing +Z,
        // local +Y up; texture V grows downward.
        Vector3 local = ToLocal(worldPosition);
        float u = Mathf.Clamp(local.X / PanelSize.X + 0.5f, 0f, 1f);
        float v = Mathf.Clamp(0.5f - local.Y / PanelSize.Y, 0f, 1f);
        return new Vector2I((int)(u * _pixelSize.X), (int)(v * _pixelSize.Y));
    }

    public override void _ExitTree()
    {
        if (_keyboardOwner == this)
        {
            _keyboardOwner = null;
        }
        _host.Dispose();
    }
}
