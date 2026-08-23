using System;
using System.IO;
using System.Text;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Serves XAML files to Noesis from the Godot virtual filesystem (res://), so XAML ships inside the exported PCK like any other asset.
///
/// URIs are resolved against ProjectSettings 'noesis_gui/resources/root' unless they are already absolute res:// paths.
///
/// Two load paths:
///  - Raw file (editor runs): direct read, hot-reload friendly.
///  - Imported XamlFile resource (exported builds): the raw .xaml isn't in the PCK, but the artifact produced by XamlImportPlugin is.
/// </summary>
public class GodotXamlProvider : Noesis.XamlProvider
{
    public override Stream LoadXaml(System.Uri uri)
    {
        string resPath = GodotResourceUtil.ToResPath(uri);

        if (Godot.FileAccess.FileExists(resPath))
        {
            return GodotResourceUtil.OpenRead(resPath, "XAML");
        }

        if (ResourceLoader.Exists(resPath) &&
            ResourceLoader.Load(resPath) is XamlFile xamlFile)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(xamlFile.Source), writable: false);
        }

        // Embedded official theme (Noesis.App.Theme assembly), e.g. "Theme/NoesisTheme.DarkBlue.xaml" and the dictionaries it merges.
        Stream themeStream = NoesisThemeResources.OpenXaml(GodotResourceUtil.GetRawPath(uri));
        if (themeStream != null)
        {
            return themeStream;
        }

        GD.PushWarning($"[NoesisGUI] XAML not found: {resPath}");
        return null;
    }
}

/// <summary>Shared res:// resolution helpers for the providers.</summary>
public static class GodotResourceUtil
{
    private const string ResPrefix = "res://";

    public static string ToResPath(System.Uri uri)
    {
        // Noesis passes relative URIs like "MainMenu.xaml" or "Images/logo.png", possibly with a leading '/'. Absolute res:// paths pass through.
        return ToResPath(GetRawPath(uri));
    }

    /// <summary>
    /// Normalizes a Noesis URI while preserving the authority portion of Godot's res:// scheme.
    /// System.Uri.AbsolutePath alone would turn "res://UI/Main.xaml" into "/Main.xaml" and lose "UI".
    /// </summary>
    public static string GetRawPath(System.Uri uri)
    {
        if (uri == null)
        {
            return "";
        }

        string raw = uri.IsAbsoluteUri && !uri.Scheme.Equals("res", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsolutePath
            : uri.OriginalString;
        return NormalizePath(raw);
    }

    public static string ToResPath(string raw)
    {
        raw = NormalizePath(raw);

        if (IsResPath(raw))
        {
            return raw;
        }

        string root = NormalizePath(NoesisServer.GetSetting("noesis_gui/resources/root", "res://UI"));
        return JoinPath(root, raw);
    }

    public static string NormalizePath(string raw)
    {
        string normalized = (raw ?? "").Replace('\\', '/');
        if (normalized.StartsWith(ResPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResPrefix + normalized[ResPrefix.Length..].TrimStart('/');
        }
        return normalized.TrimStart('/');
    }

    public static bool IsResPath(string path) =>
        path?.StartsWith(ResPrefix, StringComparison.OrdinalIgnoreCase) == true;

    public static string JoinPath(string basePath, string relativePath)
    {
        basePath = NormalizePath(basePath);
        relativePath = NormalizePath(relativePath);

        if (IsResPath(relativePath) || string.IsNullOrEmpty(basePath))
        {
            return relativePath;
        }
        if (string.IsNullOrEmpty(relativePath))
        {
            return basePath;
        }

        return basePath.EndsWith('/')
            ? basePath + relativePath
            : $"{basePath}/{relativePath}";
    }

    public static Stream OpenRead(string resPath, string kind)
    {
        if (!Godot.FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"[NoesisGUI] {kind} not found: {resPath}");
            return null; // Noesis treats null as "not found"
        }

        byte[] bytes = Godot.FileAccess.GetFileAsBytes(resPath);
        return new MemoryStream(bytes, writable: false);
    }
}
