using System;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Global NoesisGUI initialization: license, log routing, resource providers. Called lazily by the first NoesisView;
/// safe to call multiple times.
/// </summary>
public static class NoesisServer
{
    private static readonly object Lock = new();
    private static bool _initialized;

    public static bool IsInitialized
    {
        get
        {
            lock (Lock) return _initialized;
        }
    }

    public static void EnsureInitialized()
    {
        lock (Lock)
        {
            if (_initialized)
            {
                return;
            }

            // License -------------------------------------------------
            // Project Settings first, environment variables as fallback so keys can stay out of version control.
            string licenseName = GetSetting("noesisgui/license/name",
                System.Environment.GetEnvironmentVariable("NOESIS_LICENSE_NAME") ?? "");
            string licenseKey = GetSetting("noesisgui/license/key",
                System.Environment.GetEnvironmentVariable("NOESIS_LICENSE_KEY") ?? "");

            if (string.IsNullOrEmpty(licenseName) || string.IsNullOrEmpty(licenseKey))
            {
                GD.PushWarning("[NoesisGUI] No license configured. Set 'noesisgui/license/name' and " +
                               "'noesisgui/license/key' in Project Settings, or the NOESIS_LICENSE_NAME / " +
                               "NOESIS_LICENSE_KEY environment variables. Noesis will run in evaluation mode " +
                               "or refuse to start depending on SDK build.");
            }

            // Logging -------------------------------------------------
            Noesis.Log.SetLogCallback((level, channel, message) =>
            {
                switch (level)
                {
                    case Noesis.LogLevel.Error:
                        GD.PushError($"[Noesis] {message}");
                        break;
                    case Noesis.LogLevel.Warning:
                        GD.PushWarning($"[Noesis] {message}");
                        break;
                    default:
                        GD.Print($"[Noesis:{channel}] {message}");
                        break;
                }
            });

            Noesis.GUI.SetLicense(licenseName, licenseKey);
            Noesis.GUI.Init();

            // Resource providers ---------------------------------------
            // All XAML/image/font URIs resolve against res:// paths, so assets live inside the Godot project and ship in the PCK.
            Noesis.GUI.SetXamlProvider(new GodotXamlProvider());
            Noesis.GUI.SetTextureProvider(new GodotTextureProvider());
            Noesis.GUI.SetFontProvider(new GodotFontProvider());

            // Sensible text defaults (mirrors Noesis samples).
            Noesis.GUI.SetFontFallbacks(["Arial", "Segoe UI Emoji"]);
            Noesis.GUI.SetFontDefaultProperties(15.0f,
                Noesis.FontWeight.Normal, Noesis.FontStretch.Normal, Noesis.FontStyle.Normal);

            // Optional: load the Noesis theme so implicit control styles exist.
            // Requires the Noesis.App theme package. Without a theme (or your own ResourceDictionary), templated controls like Button need explicit styles.
            // Noesis.GUI.LoadApplicationResources("Theme/NoesisTheme.DarkBlue.xaml");

            _initialized = true;
            GD.Print("[NoesisGUI] Initialized.");
        }
    }

    internal static string GetSetting(string name, string fallback)
    {
        if (ProjectSettings.HasSetting(name))
        {
            string value = (string)ProjectSettings.GetSetting(name);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        return fallback;
    }
}
