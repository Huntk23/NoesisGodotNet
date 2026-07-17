using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace NoesisGodot;

/// <summary>
/// XAML hot-reload: watches the resource root for .xaml changes (editor-run builds only).
///
/// Flow: FileSystemWatcher thread enqueues changed paths → NoesisView calls Pump() once per frame on the main thread ->
/// we notify Noesis via XamlProvider.RaiseXamlChanged (updates ResourceDictionaries, styles, templates) and fully reload
/// any NoesisView, whose root XAML changed.
/// </summary>
public static class NoesisHotReload
{
    private static FileSystemWatcher _watcher;
    private static readonly ConcurrentQueue<string> Changed = new();
    private static readonly List<NoesisView> Views = new();
    private static GodotXamlProvider _provider;
    private static string _rootGlobal;
    private static ulong _lastPumpFrame = ulong.MaxValue;

    public static void Start(GodotXamlProvider provider)
    {
        if (_watcher != null || !OS.HasFeature("editor"))
        {
            return;
        }

        _provider = provider;
        string rootRes = NoesisServer.GetSetting("noesis_gui/resources/root", "res://UI");
        _rootGlobal = ProjectSettings.GlobalizePath(rootRes).Replace('\\', '/');

        if (!Directory.Exists(_rootGlobal))
        {
            return;
        }

        _watcher = new FileSystemWatcher(_rootGlobal, "*.xaml")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        _watcher.Changed += (_, e) => Changed.Enqueue(e.FullPath);
        _watcher.Created += (_, e) => Changed.Enqueue(e.FullPath);
        _watcher.Renamed += (_, e) => Changed.Enqueue(e.FullPath);
        _watcher.EnableRaisingEvents = true;

        GD.Print($"[NoesisGUI] XAML hot-reload watching '{_rootGlobal}'.");
    }

    public static void Register(NoesisView view) => Views.Add(view);
    public static void Unregister(NoesisView view) => Views.Remove(view);

    /// <summary>Project Settings → NoesisGUI → Logging → Silence Hot Reload.</summary>
    internal static bool Silenced =>
        NoesisServer.GetSettingBool("noesis_gui/logging/silence_hot_reload", false);

    /// <summary>Called by NoesisView._Process; runs at most once per frame.</summary>
    public static void Pump()
    {
        if (Changed.IsEmpty)
        {
            return;
        }

        ulong frame = Engine.GetProcessFrames();
        if (frame == _lastPumpFrame)
        {
            return;
        }
        _lastPumpFrame = frame;

        // Editors fire several events per save — dedupe this frame's batch.
        var batch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (Changed.TryDequeue(out string path))
        {
            string rel = ToRelative(path);
            if (rel != null)
            {
                batch.Add(rel);
            }
        }

        bool silenced = Silenced;
        foreach (string rel in batch)
        {
            if (!silenced)
            {
                GD.Print($"[NoesisGUI] Hot-reloading '{rel}'");
            }

            // Refresh anything Noesis has cached from this URI
            // (resource dictionaries, control templates, UserControls).
            try
            {
                _provider?.RaiseXamlChanged(new Uri(rel, UriKind.RelativeOrAbsolute));
            }
            catch (Exception e)
            {
                if (!silenced)
                {
                    GD.PushWarning($"[NoesisGUI] RaiseXamlChanged('{rel}') failed: {e.Message}");
                }
            }

            // Views whose root document changed get rebuilt outright.
            foreach (NoesisView view in Views)
            {
                if (MatchesView(view, rel))
                {
                    view.ReloadXaml();
                }
            }
        }
    }

    private static string ToRelative(string fullPath)
    {
        string p = fullPath.Replace('\\', '/');
        if (!p.StartsWith(_rootGlobal, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return p.Substring(_rootGlobal.Length).TrimStart('/');
    }

    private static bool MatchesView(NoesisView view, string rel)
    {
        string x = (view.Xaml ?? "").Replace('\\', '/').TrimStart('/');
        if (x.StartsWith("res://"))
        {
            // Absolute res:// path: compare via the provider root.
            string abs = GodotResourceUtil.ToResPath(rel);
            return string.Equals(x, abs, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(x, rel, StringComparison.OrdinalIgnoreCase);
    }
}
