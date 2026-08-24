using Godot;
using NoesisGodot;

namespace NoesisGodot.Tests;

public sealed class NoesisInputMapperTests
{
    public static TheoryData<Key, Noesis.Key> KeyCases => new()
    {
        { Key.Escape, Noesis.Key.Escape },
        { Key.Enter, Noesis.Key.Enter },
        { Key.KpEnter, Noesis.Key.Enter },
        { Key.Backspace, Noesis.Key.Back },
        { Key.Left, Noesis.Key.Left },
        { Key.Pageup, Noesis.Key.PageUp },
        { Key.Ctrl, Noesis.Key.LeftCtrl },
        { Key.Space, Noesis.Key.Space },
        { Key.Apostrophe, Noesis.Key.OemQuotes },
        { Key.Bracketright, Noesis.Key.OemCloseBrackets },
        { Key.Key0, Noesis.Key.D0 },
        { Key.Key9, Noesis.Key.D9 },
        { Key.A, Noesis.Key.A },
        { Key.Z, Noesis.Key.Z },
        { Key.Kp0, Noesis.Key.NumPad0 },
        { Key.KpPeriod, Noesis.Key.Decimal },
        { Key.F1, Noesis.Key.F1 },
        { Key.F12, Noesis.Key.F12 },
        { (Key) 0x7fff_ffff, Noesis.Key.None },
    };

    public static TheoryData<JoyButton, Noesis.Key> JoyButtonCases => new()
    {
        { JoyButton.A, Noesis.Key.GamepadAccept },
        { JoyButton.B, Noesis.Key.GamepadCancel },
        { JoyButton.X, Noesis.Key.GamepadContext1 },
        { JoyButton.Y, Noesis.Key.GamepadContext2 },
        { JoyButton.DpadUp, Noesis.Key.GamepadUp },
        { JoyButton.DpadDown, Noesis.Key.GamepadDown },
        { JoyButton.DpadLeft, Noesis.Key.GamepadLeft },
        { JoyButton.DpadRight, Noesis.Key.GamepadRight },
        { JoyButton.LeftShoulder, Noesis.Key.GamepadPageLeft },
        { JoyButton.RightShoulder, Noesis.Key.GamepadPageRight },
        { JoyButton.Start, Noesis.Key.GamepadMenu },
        { JoyButton.Back, Noesis.Key.GamepadView },
        { (JoyButton) 999, Noesis.Key.None },
    };

    [Theory]
    [MemberData(nameof(KeyCases))]
    public void MapKey_MapsRepresentativeKeyboardCategories(Key key, Noesis.Key expected)
    {
        Assert.Equal(expected, NoesisInputMapper.MapKey(key));
    }

    [Theory]
    [MemberData(nameof(JoyButtonCases))]
    public void MapJoyButton_MapsNavigationControls(JoyButton button, Noesis.Key expected)
    {
        Assert.Equal(expected, NoesisInputMapper.MapJoyButton(button));
    }
}
