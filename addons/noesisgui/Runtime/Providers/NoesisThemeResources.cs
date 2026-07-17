using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace NoesisGodot;

/// <summary>
/// Access to the official Noesis theme embedded in the Noesis.App.Theme assembly (NuGet package of the same name). Mirrors the resource-name
/// conventions of NoesisApp's EmbeddedXamlProvider / EmbeddedFontProvider:
///   XAML:  ""  + "Theme/NoesisTheme.DarkBlue.xaml" → "Theme.NoesisTheme.DarkBlue.xaml"
///   Fonts: "Noesis.GUI.Extensions" + "Theme/Fonts/X.ttf" → "Noesis.GUI.Extensions.Theme.Fonts.X.ttf"
/// </summary>
internal static class NoesisThemeResources
{
    private const string AssemblyName = "Noesis.App.Theme";
    private const string FontNamespace = "Noesis.GUI.Extensions";

    private static Assembly _assembly;
    private static bool _loadAttempted;

    public static Assembly Assembly
    {
        get
        {
            if (!_loadAttempted)
            {
                _loadAttempted = true;
                try
                {
                    _assembly = Assembly.Load(AssemblyName);
                }
                catch
                {
                    _assembly = null; // package isn't referenced — theme disabled
                }
            }
            return _assembly;
        }
    }

    public static Stream OpenXaml(string path) => Open("", path);

    public static Stream OpenFont(string path) => Open(FontNamespace, path);

    /// <summary>Enumerates embedded theme fonts as (folder, filename), e.g. ("Theme/Fonts", "PT Root UI_Regular.ttf").</summary>
    public static IEnumerable<(string Folder, string Filename)> EnumerateFonts()
    {
        Assembly asm = Assembly;
        if (asm == null)
        {
            yield break;
        }

        foreach (string name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Same parsing as NoesisApp.EmbeddedFontProvider
            string resource = name.StartsWith(FontNamespace + ".") ? name.Substring(FontNamespace.Length + 1) : name;
            int lastDot = resource.LastIndexOf('.', resource.Length - 5);
            string folder = lastDot != -1 ? resource.Substring(0, lastDot).Replace('.', '/') : "";
            string filename = lastDot != -1 ? resource.Substring(lastDot + 1) : resource;
            yield return (folder, filename);
        }
    }

    private static Stream Open(string ns, string path)
    {
        Assembly asm = Assembly;
        if (asm == null || string.IsNullOrEmpty(path))
        {
            return null;
        }

        string resource = string.IsNullOrEmpty(ns)
            ? path.Replace('/', '.')
            : ns + "." + path.Replace('/', '.');

        try
        {
            return asm.GetManifestResourceStream(resource);
        }
        catch
        {
            return null;
        }
    }
}
