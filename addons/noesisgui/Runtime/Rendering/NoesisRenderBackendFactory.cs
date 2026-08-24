using System;
using System.Collections.Generic;
using Godot;

namespace NoesisGodot;

internal enum NoesisHostPlatform
{
    Unsupported = 0,
    Windows,
    Linux,
    MacOs,
}

internal readonly record struct NoesisBackendProbe(bool IsSupported, string Reason)
{
    public static NoesisBackendProbe Supported() => new(true, "");

    public static NoesisBackendProbe Unsupported(string reason) => new(false, reason);
}

internal sealed class NoesisRenderBackendResult
{
    public INoesisRenderBackend Backend { get; }

    public NoesisRenderingStatus Status { get; }

    public Exception Error { get; }

    public bool Succeeded => Backend != null;

    private NoesisRenderBackendResult(INoesisRenderBackend backend, NoesisRenderingStatus status, Exception error)
    {
        Backend = backend;
        Status = status;
        Error = error;
    }

    public static NoesisRenderBackendResult Success(INoesisRenderBackend backend, NoesisRenderingStatus status) =>
        new(backend, status, null);

    public static NoesisRenderBackendResult Failure(NoesisRenderingStatus status, Exception error) => new(null, status, error);
}

/// <summary>Owns backend capability probing, deterministic candidate ordering, initialization, and fallback.</summary>
internal static class NoesisRenderBackendFactory
{
    internal static IReadOnlyList<NoesisRenderBackendKind> GetCandidateOrder(NoesisHostPlatform platform, bool zeroCopyEnabled, bool sharedOpenGlSupported, bool vulkanInteropSupported)
    {
        var candidates = new List<NoesisRenderBackendKind>(3);

        if (platform == NoesisHostPlatform.Windows)
        {
            if (zeroCopyEnabled && sharedOpenGlSupported)
            {
                candidates.Add(NoesisRenderBackendKind.SharedOpenGl);
            }

            if (zeroCopyEnabled && vulkanInteropSupported)
            {
                candidates.Add(NoesisRenderBackendKind.VulkanOpenGlInterop);
            }

            candidates.Add(NoesisRenderBackendKind.WindowsOpenGlReadback);
        }
        else if (platform == NoesisHostPlatform.Linux)
        {
            candidates.Add(NoesisRenderBackendKind.LinuxEglReadback);
        }

        return candidates;
    }

    public static NoesisRenderBackendResult Create(int width, int height, string ownerName, bool zeroCopyEnabled)
    {
        NoesisHostPlatform platform = DetectPlatform();
        NoesisBackendProbe sharedOpenGl = NoesisBackendProbe.Unsupported("Shared OpenGL was not probed.");
        NoesisBackendProbe vulkanInterop = NoesisBackendProbe.Unsupported("Vulkan/OpenGL interop was not probed.");

        if (platform == NoesisHostPlatform.Windows && zeroCopyEnabled)
        {
            sharedOpenGl = SharedGLBackend.Probe();
            vulkanInterop = VkSharedGLBackend.Probe();
        }

        IReadOnlyList<NoesisRenderBackendKind> candidates = GetCandidateOrder(platform, zeroCopyEnabled, sharedOpenGl.IsSupported, vulkanInterop.IsSupported);
        if (candidates.Count == 0)
        {
            string detail = platform == NoesisHostPlatform.MacOs ? "No macOS render backend is available yet." : "No render backend is available on this operating system.";
            var status = NoesisRenderingStatus.Unavailable(detail);
            return NoesisRenderBackendResult.Failure(status, new PlatformNotSupportedException(detail));
        }

        var failures = new List<string>();
        Exception lastError = null;

        for (var index = 0; index < candidates.Count; index++)
        {
            NoesisRenderBackendKind kind = candidates[index];
            INoesisRenderBackend backend = CreateBackend(kind);
            try
            {
                backend.Init(width, height);
            }
            catch (Exception exception)
            {
                lastError = exception;
                failures.Add($"{NoesisRenderingStatus.GetBackendName(kind)} initialization failed: {exception.Message}");
                TryDispose(backend, ownerName);

                if (index + 1 < candidates.Count)
                {
                    string nextName = NoesisRenderingStatus.GetBackendName(candidates[index + 1]);
                    GD.PushWarning($"[NoesisGUI] {ownerName}: {failures[^1]}; trying {nextName}.");
                }

                continue;
            }

            string detail = BuildSelectionDetail(platform, zeroCopyEnabled, sharedOpenGl, vulkanInterop, kind, failures);
            NoesisRenderingStatus status = NoesisRenderingStatus.Ready(kind, detail);
            GD.Print($"[NoesisGUI] {ownerName}: {status}");
            return NoesisRenderBackendResult.Success(backend, status);
        }

        string failureDetail = $"No render backend could be initialized. {string.Join(" ", failures)}";
        var failureStatus = NoesisRenderingStatus.Unavailable(failureDetail);
        var error = new InvalidOperationException(failureDetail, lastError);
        return NoesisRenderBackendResult.Failure(failureStatus, error);
    }

    private static string BuildSelectionDetail(NoesisHostPlatform platform, bool zeroCopyEnabled, NoesisBackendProbe sharedOpenGl, NoesisBackendProbe vulkanInterop, NoesisRenderBackendKind selected, IReadOnlyCollection<string> failures)
    {
        if (selected is NoesisRenderBackendKind.SharedOpenGl or NoesisRenderBackendKind.VulkanOpenGlInterop)
        {
            return failures.Count == 0 ? "" : string.Join(" ", failures);
        }

        var details = new List<string>(failures);
        if (!zeroCopyEnabled)
        {
            details.Add("Zero-copy is disabled by 'noesis_gui/rendering/zero_copy'.");
        }
        else if (platform == NoesisHostPlatform.Windows)
        {
            if (!sharedOpenGl.IsSupported)
            {
                details.Add($"Shared OpenGL unavailable: {sharedOpenGl.Reason}");
            }

            if (!vulkanInterop.IsSupported)
            {
                details.Add($"Vulkan/OpenGL interop unavailable: {vulkanInterop.Reason}");
            }
        }
        else if (platform == NoesisHostPlatform.Linux)
        {
            details.Add("No Linux zero-copy backend is available yet.");
        }

        return string.Join(" ", details);
    }

    private static INoesisRenderBackend CreateBackend(NoesisRenderBackendKind kind) => kind switch
    {
        NoesisRenderBackendKind.SharedOpenGl => new SharedGLBackend(),
        NoesisRenderBackendKind.VulkanOpenGlInterop => new VkSharedGLBackend(),
        NoesisRenderBackendKind.WindowsOpenGlReadback => new OffscreenGLBackend(),
        NoesisRenderBackendKind.LinuxEglReadback => new EglOffscreenBackend(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Noesis render backend."),
    };

    private static NoesisHostPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return NoesisHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return NoesisHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return NoesisHostPlatform.MacOs;
        }

        return NoesisHostPlatform.Unsupported;
    }

    private static void TryDispose(INoesisRenderBackend backend, string ownerName)
    {
        try
        {
            backend.Dispose();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[NoesisGUI] {ownerName}: failed backend initialization cleanup: {exception.Message}");
        }
    }
}
