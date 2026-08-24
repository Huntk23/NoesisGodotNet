namespace NoesisGodot;

/// <summary>The render backend selected for a hosted Noesis view.</summary>
public enum NoesisRenderBackendKind
{
    None = 0,
    SharedOpenGl,
    VulkanOpenGlInterop,
    WindowsOpenGlReadback,
    LinuxEglReadback,
}

/// <summary>How rendered pixels reach the Godot texture.</summary>
public enum NoesisRenderTransferMode
{
    None = 0,
    ZeroCopy,
    CpuReadback,
}

/// <summary>
/// Runtime information about the backend selected for a view. This is intended for diagnostics and support tooling;
/// games do not need to branch their UI behavior on it.
/// </summary>
public sealed class NoesisRenderingStatus
{
    internal static NoesisRenderingStatus NotInitialized { get; } = new(false, NoesisRenderBackendKind.None, NoesisRenderTransferMode.None, "Not initialized", "");

    public bool IsReady { get; }

    public bool IsZeroCopy => TransferMode == NoesisRenderTransferMode.ZeroCopy;

    public NoesisRenderBackendKind Backend { get; }

    public NoesisRenderTransferMode TransferMode { get; }

    public string BackendName { get; }

    /// <summary>Why this backend was selected, or why rendering is unavailable.</summary>
    public string Detail { get; }

    private NoesisRenderingStatus(bool isReady, NoesisRenderBackendKind backend, NoesisRenderTransferMode transferMode, string backendName, string detail)
    {
        IsReady = isReady;
        Backend = backend;
        TransferMode = transferMode;
        BackendName = backendName;
        Detail = detail ?? "";
    }

    internal static NoesisRenderingStatus Ready(NoesisRenderBackendKind backend, string detail)
    {
        NoesisRenderTransferMode transferMode = backend is NoesisRenderBackendKind.SharedOpenGl or NoesisRenderBackendKind.VulkanOpenGlInterop ? NoesisRenderTransferMode.ZeroCopy : NoesisRenderTransferMode.CpuReadback;

        return new NoesisRenderingStatus(true, backend, transferMode, GetBackendName(backend), detail);
    }

    internal static NoesisRenderingStatus Unavailable(string detail) =>
        new(false, NoesisRenderBackendKind.None, NoesisRenderTransferMode.None, "Unavailable", detail);

    public override string ToString()
    {
        if (!IsReady)
        {
            return string.IsNullOrWhiteSpace(Detail) ? BackendName : $"{BackendName}: {Detail}";
        }

        string transfer = TransferMode == NoesisRenderTransferMode.ZeroCopy ? "zero-copy" : "CPU readback";
        string summary = $"{BackendName} selected ({transfer})";
        return string.IsNullOrWhiteSpace(Detail) ? summary : $"{summary}. {Detail}";
    }

    internal static string GetBackendName(NoesisRenderBackendKind backend) => backend switch
    {
        NoesisRenderBackendKind.SharedOpenGl => "Shared OpenGL",
        NoesisRenderBackendKind.VulkanOpenGlInterop => "Vulkan/OpenGL interop",
        NoesisRenderBackendKind.WindowsOpenGlReadback => "Windows offscreen OpenGL",
        NoesisRenderBackendKind.LinuxEglReadback => "Linux EGL offscreen",
        _ => "Unavailable",
    };
}
