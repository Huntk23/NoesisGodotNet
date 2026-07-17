using System.IO;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Exposes .ttf/.otf files under 'noesis_gui/resources/fonts' (and any folder referenced by XAML font URIs) to Noesis text rendering.
///
/// XAML usage: FontFamily="./#My Font Name"  (family name, not filename - same convention as WPF/Noesis).
/// </summary>
public class GodotFontProvider : Noesis.FontProvider
{
    public GodotFontProvider()
    {
        // Register the official theme's embedded fonts (PT Root UI etc.) so themed text renders as designed. No-op if the package is absent.
        int count = 0;
        foreach ((string folder, string filename) in NoesisThemeResources.EnumerateFonts())
        {
            RegisterFont(new System.Uri(folder, System.UriKind.RelativeOrAbsolute), filename);
            GD.Print($"[NoesisGUI] Theme font registered: '{folder}/{filename}'");
            count++;
        }
        if (count == 0)
        {
            GD.Print("[NoesisGUI] No embedded theme fonts found (Noesis.App.Theme absent?).");
        }
    }

    // NOTE: newer SDKs (post-3.2) normalize base URIs by overriding MatchFont/FamilyExists (returning Noesis.FontSource); that type doesn't exist
    // in 3.2.x, so we rely on the base implementation's matching. If theme fonts ever fall back to Arial, revisit this against the SDK version.
    public override void ScanFolder(System.Uri folder)
    {
        string resFolder = ResolveFolder(folder);
        using var dir = DirAccess.Open(resFolder);
        if (dir == null)
        {
            return; // no fonts folder is fine — system fallbacks still apply
        }

        dir.ListDirBegin();
        for (string file = dir.GetNext(); !string.IsNullOrEmpty(file); file = dir.GetNext())
        {
            if (dir.CurrentIsDir())
            {
                continue;
            }
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".ttf" or ".otf" or ".ttc")
            {
                RegisterFont(folder, file);
            }
        }
        dir.ListDirEnd();
    }

    public override Stream OpenFont(System.Uri folder, string filename)
    {
        // Embedded theme fonts first: their folder ("Theme/Fonts") is not a res:// location.
        string rawFolder = folder == null ? "" : GodotResourceUtil.GetRawPath(folder);
        Stream themeFont = NoesisThemeResources.OpenFont(
            string.IsNullOrEmpty(rawFolder) ? filename : $"{rawFolder.TrimEnd('/')}/{filename}");
        if (themeFont != null)
        {
            return themeFont;
        }

        string resFolder = ResolveFolder(folder);
        return GodotResourceUtil.OpenRead($"{resFolder.TrimEnd('/')}/{filename}", "Font");
    }

    private static string ResolveFolder(System.Uri folder)
    {
        string raw = folder == null ? "" : folder.IsAbsoluteUri ? folder.AbsolutePath : folder.OriginalString;
        raw = raw.Replace('\\', '/').Trim('/');

        if (raw.StartsWith("res://"))
        {
            return raw;
        }
        if (string.IsNullOrEmpty(raw) || raw == ".")
        {
            return NoesisServer.GetSetting("noesis_gui/resources/fonts", "res://UI/Fonts");
        }

        string root = NoesisServer.GetSetting("noesis_gui/resources/root", "res://UI");
        return $"{root.TrimEnd('/')}/{raw}";
    }
}
