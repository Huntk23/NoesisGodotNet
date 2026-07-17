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
    private static GodotXamlProvider _xamlProvider;

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
            string licenseName = GetSetting("noesis_gui/license/name",
                System.Environment.GetEnvironmentVariable("NOESIS_LICENSE_NAME") ?? "");
            string licenseKey = GetSetting("noesis_gui/license/key",
                System.Environment.GetEnvironmentVariable("NOESIS_LICENSE_KEY") ?? "");

            if (string.IsNullOrEmpty(licenseName) || string.IsNullOrEmpty(licenseKey))
            {
                GD.PushWarning("[NoesisGUI] No license configured. Set 'noesis_gui/license/name' and " +
                               "'noesis_gui/license/key' in Project Settings, or the NOESIS_LICENSE_NAME / " +
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
            _xamlProvider = new GodotXamlProvider();
            Noesis.GUI.SetXamlProvider(_xamlProvider);
            Noesis.GUI.SetTextureProvider(new GodotTextureProvider());
            Noesis.GUI.SetFontProvider(new GodotFontProvider());

            // Sensible text defaults (mirrors Noesis samples).
            Noesis.GUI.SetFontFallbacks(["Arial", "Segoe UI Emoji"]);
            Noesis.GUI.SetFontDefaultProperties(15.0f,
                Noesis.FontWeight.Normal, Noesis.FontStretch.Normal, Noesis.FontStyle.Normal);

            // Theme: implicit styles/templates for all controls. Without one, untemplated controls render as Noesis's pink fallback.
            // Default is the official theme embedded in Noesis.GUI.Extensions; set the project setting to "" to opt out, or point it at your own
            // ResourceDictionary (res:// paths work through the providers). Read directly (not GetSetting): an explicit EMPTY value means
            // "no theme", which GetSetting would override with the fallback.
            string theme = ProjectSettings.HasSetting("noesis_gui/theme/xaml")
                ? (string)ProjectSettings.GetSetting("noesis_gui/theme/xaml")
                : "Theme/NoesisTheme.DarkBlue.xaml";
            if (!string.IsNullOrEmpty(theme))
            {
                try
                {
                    Noesis.GUI.LoadApplicationResources(theme);
                }
                catch (Exception e)
                {
                    GD.PushWarning($"[NoesisGUI] Failed to load theme '{theme}': {e.Message}. " +
                                   "Controls without explicit templates will render pink.");
                }
            }

            _initialized = true;
            GD.Print("[NoesisGUI] Initialized.");
        }

        NoesisHotReload.Start(_xamlProvider);
    }

    internal static bool GetSettingBool(string name, bool fallback)
    {
        if (ProjectSettings.HasSetting(name))
        {
            return (bool)ProjectSettings.GetSetting(name);
        }
        return fallback;
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
