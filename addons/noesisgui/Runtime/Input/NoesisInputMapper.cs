using Godot;

namespace NoesisGodot;

/// <summary>
/// Translates Godot input events into Noesis view input. Positions from _GuiInput are already in Control-local coordinates,
/// which match the Noesis view surface 1:1 (NoesisView keeps view size == control size).
/// </summary>
public static class NoesisInputMapper
{
    /// <returns>true if the event was forwarded (caller should AcceptEvent()).</returns>
    public static bool Forward(Noesis.View view, InputEvent e, Control source)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
            {
                var p = motion.Position;
                view.MouseMove((int)p.X, (int)p.Y);
                return true;
            }

            case InputEventMouseButton button:
            {
                var p = button.Position;
                int x = (int)p.X, y = (int)p.Y;

                switch (button.ButtonIndex)
                {
                    case MouseButton.WheelUp:
                        if (button.Pressed) view.Scroll(x, y, WheelDelta(button));
                        return true;
                    case MouseButton.WheelDown:
                        if (button.Pressed) view.Scroll(x, y, -WheelDelta(button));
                        return true;
                    case MouseButton.WheelLeft:
                        if (button.Pressed) view.HScroll(x, y, -120);
                        return true;
                    case MouseButton.WheelRight:
                        if (button.Pressed) view.HScroll(x, y, 120);
                        return true;
                }

                Noesis.MouseButton nsButton = button.ButtonIndex switch
                {
                    MouseButton.Left => Noesis.MouseButton.Left,
                    MouseButton.Right => Noesis.MouseButton.Right,
                    MouseButton.Middle => Noesis.MouseButton.Middle,
                    MouseButton.Xbutton1 => Noesis.MouseButton.XButton1,
                    MouseButton.Xbutton2 => Noesis.MouseButton.XButton2,
                    _ => Noesis.MouseButton.Left,
                };

                if (button.Pressed)
                {
                    if (button.DoubleClick)
                    {
                        view.MouseDoubleClick(x, y, nsButton);
                    }
                    else
                    {
                        view.MouseButtonDown(x, y, nsButton);
                    }
                }
                else
                {
                    view.MouseButtonUp(x, y, nsButton);
                }
                return true;
            }

            case InputEventKey key:
            {
                Noesis.Key nsKey = MapKey(key.Keycode);

                if (key.Pressed)
                {
                    bool handled = false;
                    if (nsKey != Noesis.Key.None)
                    {
                        handled |= view.KeyDown(nsKey);
                    }
                    // Text input (TextBox etc.)
                    if (key.Unicode != 0)
                    {
                        handled |= view.Char((uint)key.Unicode);
                    }
                    return handled || nsKey != Noesis.Key.None;
                }

                if (nsKey != Noesis.Key.None)
                {
                    view.KeyUp(nsKey);
                    return true;
                }

                break;
            }

            case InputEventScreenTouch touch:
            {
                var p = touch.Position;
                if (touch.Pressed)
                {
                    view.TouchDown((int)p.X, (int)p.Y, (ulong)touch.Index);
                }
                else
                {
                    view.TouchUp((int)p.X, (int)p.Y, (ulong)touch.Index);
                }
                return true;
            }

            case InputEventScreenDrag drag:
            {
                var p = drag.Position;
                view.TouchMove((int)p.X, (int)p.Y, (ulong)drag.Index);
                return true;
            }
        }

        return false;
    }

    /// <summary>Standard 120-unit wheel notch, scaled by a precision-scroll factor.</summary>
    private static int WheelDelta(InputEventMouseButton b)
    {
        float factor = b.Factor > 0f ? b.Factor : 1f;
        return (int)(120f * factor);
    }

    private static Noesis.Key MapKey(Key key) => key switch
    {
        Key.Escape => Noesis.Key.Escape,
        Key.Tab => Noesis.Key.Tab,
        Key.Backspace => Noesis.Key.Back,
        Key.Enter => Noesis.Key.Enter,
        Key.KpEnter => Noesis.Key.Enter,
        Key.Insert => Noesis.Key.Insert,
        Key.Delete => Noesis.Key.Delete,
        Key.Pause => Noesis.Key.Pause,
        Key.Home => Noesis.Key.Home,
        Key.End => Noesis.Key.End,
        Key.Left => Noesis.Key.Left,
        Key.Up => Noesis.Key.Up,
        Key.Right => Noesis.Key.Right,
        Key.Down => Noesis.Key.Down,
        Key.Pageup => Noesis.Key.PageUp,
        Key.Pagedown => Noesis.Key.PageDown,
        Key.Shift => Noesis.Key.LeftShift,
        Key.Ctrl => Noesis.Key.LeftCtrl,
        Key.Alt => Noesis.Key.LeftAlt,
        Key.Capslock => Noesis.Key.CapsLock,
        Key.Numlock => Noesis.Key.NumLock,
        Key.Scrolllock => Noesis.Key.Scroll,
        Key.Space => Noesis.Key.Space,
        Key.Apostrophe => Noesis.Key.OemQuotes,
        Key.Comma => Noesis.Key.OemComma,
        Key.Minus => Noesis.Key.OemMinus,
        Key.Period => Noesis.Key.OemPeriod,
        Key.Slash => Noesis.Key.OemQuestion,
        Key.Semicolon => Noesis.Key.OemSemicolon,
        Key.Equal => Noesis.Key.OemPlus,
        Key.Bracketleft => Noesis.Key.OemOpenBrackets,
        Key.Backslash => Noesis.Key.OemBackslash,
        Key.Bracketright => Noesis.Key.OemCloseBrackets,
        Key.Quoteleft => Noesis.Key.OemTilde,

        Key.Key0 => Noesis.Key.D0,
        Key.Key1 => Noesis.Key.D1,
        Key.Key2 => Noesis.Key.D2,
        Key.Key3 => Noesis.Key.D3,
        Key.Key4 => Noesis.Key.D4,
        Key.Key5 => Noesis.Key.D5,
        Key.Key6 => Noesis.Key.D6,
        Key.Key7 => Noesis.Key.D7,
        Key.Key8 => Noesis.Key.D8,
        Key.Key9 => Noesis.Key.D9,

        Key.A => Noesis.Key.A,
        Key.B => Noesis.Key.B,
        Key.C => Noesis.Key.C,
        Key.D => Noesis.Key.D,
        Key.E => Noesis.Key.E,
        Key.F => Noesis.Key.F,
        Key.G => Noesis.Key.G,
        Key.H => Noesis.Key.H,
        Key.I => Noesis.Key.I,
        Key.J => Noesis.Key.J,
        Key.K => Noesis.Key.K,
        Key.L => Noesis.Key.L,
        Key.M => Noesis.Key.M,
        Key.N => Noesis.Key.N,
        Key.O => Noesis.Key.O,
        Key.P => Noesis.Key.P,
        Key.Q => Noesis.Key.Q,
        Key.R => Noesis.Key.R,
        Key.S => Noesis.Key.S,
        Key.T => Noesis.Key.T,
        Key.U => Noesis.Key.U,
        Key.V => Noesis.Key.V,
        Key.W => Noesis.Key.W,
        Key.X => Noesis.Key.X,
        Key.Y => Noesis.Key.Y,
        Key.Z => Noesis.Key.Z,

        Key.Kp0 => Noesis.Key.NumPad0,
        Key.Kp1 => Noesis.Key.NumPad1,
        Key.Kp2 => Noesis.Key.NumPad2,
        Key.Kp3 => Noesis.Key.NumPad3,
        Key.Kp4 => Noesis.Key.NumPad4,
        Key.Kp5 => Noesis.Key.NumPad5,
        Key.Kp6 => Noesis.Key.NumPad6,
        Key.Kp7 => Noesis.Key.NumPad7,
        Key.Kp8 => Noesis.Key.NumPad8,
        Key.Kp9 => Noesis.Key.NumPad9,
        Key.KpMultiply => Noesis.Key.Multiply,
        Key.KpAdd => Noesis.Key.Add,
        Key.KpSubtract => Noesis.Key.Subtract,
        Key.KpDivide => Noesis.Key.Divide,
        Key.KpPeriod => Noesis.Key.Decimal,

        Key.F1 => Noesis.Key.F1,
        Key.F2 => Noesis.Key.F2,
        Key.F3 => Noesis.Key.F3,
        Key.F4 => Noesis.Key.F4,
        Key.F5 => Noesis.Key.F5,
        Key.F6 => Noesis.Key.F6,
        Key.F7 => Noesis.Key.F7,
        Key.F8 => Noesis.Key.F8,
        Key.F9 => Noesis.Key.F9,
        Key.F10 => Noesis.Key.F10,
        Key.F11 => Noesis.Key.F11,
        Key.F12 => Noesis.Key.F12,

        _ => Noesis.Key.None,
    };
}
