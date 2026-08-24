using NoesisGodot;

namespace NoesisGodot.Tests;

public sealed class GodotResourceUtilTests
{
    [Theory]
    [InlineData("res://examples/HelloNoesis/UI/MainMenu.xaml", "res://examples/HelloNoesis/UI/MainMenu.xaml")]
    [InlineData("Images\\logo.png", "Images/logo.png")]
    [InlineData("/Images/logo.png", "Images/logo.png")]
    [InlineData("https://example.test/UI/Main.xaml?theme=dark", "UI/Main.xaml")]
    public void GetRawPath_NormalizesSupportedUriForms(string value, string expected)
    {
        var uri = new Uri(value, UriKind.RelativeOrAbsolute);

        Assert.Equal(expected, GodotResourceUtil.GetRawPath(uri));
    }

    [Fact]
    public void GetRawPath_ReturnsEmptyStringForNullUri()
    {
        Assert.Equal(string.Empty, GodotResourceUtil.GetRawPath(null!));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("\\UI\\Main.xaml", "UI/Main.xaml")]
    [InlineData("RES:////UI/Main.xaml", "res://UI/Main.xaml")]
    [InlineData("res://MixedCase/Main.xaml", "res://MixedCase/Main.xaml")]
    public void NormalizePath_PreservesResourceIdentityWhileNormalizingSeparators(string? value, string expected)
    {
        Assert.Equal(expected, GodotResourceUtil.NormalizePath(value!));
    }

    [Theory]
    [InlineData("res://UI", "Main.xaml", "res://UI/Main.xaml")]
    [InlineData("res://UI/", "/Main.xaml", "res://UI/Main.xaml")]
    [InlineData("res://UI", "res://Shared/Main.xaml", "res://Shared/Main.xaml")]
    [InlineData("", "Main.xaml", "Main.xaml")]
    [InlineData("res://UI", "", "res://UI")]
    public void JoinPath_HandlesRootsAndAbsoluteOverrides(string root, string relative, string expected)
    {
        Assert.Equal(expected, GodotResourceUtil.JoinPath(root, relative));
    }

    [Theory]
    [InlineData("res://UI/Main.xaml", true)]
    [InlineData("RES://UI/Main.xaml", true)]
    [InlineData("user://UI/Main.xaml", false)]
    [InlineData("UI/Main.xaml", false)]
    [InlineData(null, false)]
    public void IsResPath_IsCaseInsensitiveAndNullSafe(string? path, bool expected)
    {
        Assert.Equal(expected, GodotResourceUtil.IsResPath(path!));
    }
}
