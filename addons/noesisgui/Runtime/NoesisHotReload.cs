using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace NoesisGodot;

/// <summary>
/// XAML hot-reload: watches the resource root for .xaml changes (editor-run builds only).
///
/// Flow: FileSystemWatcher thread enqueues changed paths → NoesisView calls Pump() once per frame on the main thread -> we notify Noesis via
/// XamlProvider.RaiseXamlChanged (updates ResourceDictionaries, styles, templates) and fully reload any NoesisView, whose root XAML changed.
/// </summary>
public static class NoesisHotReload
{
    private static FileSystemWatcher _watcher;
    private static readonly ConcurrentQueue<string> Changed = new();
    private static readonly List<NoesisViewHost> Hosts = new();
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

    public static void Register(NoesisViewHost host) => Hosts.Add(host);
    public static void Unregister(NoesisViewHost host) => Hosts.Remove(host);

    /// <summary>Find the host owning a Noesis view (used to route cursor callbacks).</summary>
    internal static NoesisViewHost FindByView(Noesis.View view)
    {
        foreach (NoesisViewHost host in Hosts)
        {
            // Managed proxies compare by underlying native instance.
            if (host.View == view || (host.View?.Equals(view) ?? false))
            {
                return host;
            }
        }
        return null;
    }

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
            // Validate BEFORE notifying Noesis: RaiseXamlChanged makes Noesis reparse the file internally, and broken markup blanks live views.
            // A failed parse here keeps the last good state on screen. Noesis's parser is LENIENT (e.g., a malformed tag) don't throw; they're
            // logged and yield a truncated tree. So we also treat any Error-level Noesis log during the parse as a validation failure.
            string parseError = null;
            int errorsBefore = NoesisServer.LogErrorCount;
            try
            {
                object parsed = Noesis.GUI.LoadXaml(rel);
                if (parsed == null)
                {
                    parseError = "file not found or empty";
                }
                else if (NoesisServer.LogErrorCount != errorsBefore)
                {
                    parseError = NoesisServer.LastLogError;
                }
            }
            catch (Exception e)
            {
                parseError = e.Message;
            }

            if (parseError != null)
            {
                if (!silenced)
                {
                    GD.PushWarning($"[NoesisGUI] '{rel}' has invalid XAML, keeping previous view: {parseError}");
                }
                foreach (NoesisViewHost host in Hosts)
                {
                    if (MatchesXaml(host.Xaml, rel))
                    {
                        host.NotifyReloadFailed(parseError);
                    }
                }
                continue;
            }

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
            foreach (NoesisViewHost host in Hosts)
            {
                if (MatchesXaml(host.Xaml, rel))
                {
                    host.ReloadXaml();
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

    private static bool MatchesXaml(string xaml, string rel)
    {
        string x = (xaml ?? "").Replace('\\', '/').TrimStart('/');
        if (x.StartsWith("res://"))
        {
            // Absolute res:// path: compare via the provider root.
            string abs = GodotResourceUtil.ToResPath(rel);
            return string.Equals(x, abs, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(x, rel, StringComparison.OrdinalIgnoreCase);
    }
}
