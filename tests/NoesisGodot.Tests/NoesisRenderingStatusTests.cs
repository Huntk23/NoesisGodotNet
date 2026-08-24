using NoesisGodot;

namespace NoesisGodot.Tests;

public sealed class NoesisRenderingStatusTests
{
    public static TheoryData<NoesisRenderBackendKind, NoesisRenderTransferMode, bool, string, string> ReadyCases => new()
    {
        {
            NoesisRenderBackendKind.SharedOpenGl, NoesisRenderTransferMode.ZeroCopy, true,
            "Shared OpenGL", "Shared OpenGL selected (zero-copy)"
        },
        {
            NoesisRenderBackendKind.VulkanOpenGlInterop, NoesisRenderTransferMode.ZeroCopy, true,
            "Vulkan/OpenGL interop", "Vulkan/OpenGL interop selected (zero-copy)"
        },
        {
            NoesisRenderBackendKind.WindowsOpenGlReadback, NoesisRenderTransferMode.CpuReadback, false,
            "Windows offscreen OpenGL", "Windows offscreen OpenGL selected (CPU readback)"
        },
        {
            NoesisRenderBackendKind.LinuxEglReadback, NoesisRenderTransferMode.CpuReadback, false,
            "Linux EGL offscreen", "Linux EGL offscreen selected (CPU readback)"
        },
    };

    [Theory]
    [MemberData(nameof(ReadyCases))]
    public void Ready_ExposesStableDiagnosticContract(NoesisRenderBackendKind backend,
        NoesisRenderTransferMode transferMode, bool isZeroCopy, string backendName, string summary)
    {
        NoesisRenderingStatus status = NoesisRenderingStatus.Ready(backend, "");

        Assert.True(status.IsReady);
        Assert.Equal(backend, status.Backend);
        Assert.Equal(transferMode, status.TransferMode);
        Assert.Equal(isZeroCopy, status.IsZeroCopy);
        Assert.Equal(backendName, status.BackendName);
        Assert.Equal(string.Empty, status.Detail);
        Assert.Equal(summary, status.ToString());
    }

    [Fact]
    public void Ready_AppendsFallbackDetailToSummary()
    {
        const string detail = "Shared OpenGL unavailable: no current context.";

        NoesisRenderingStatus status = NoesisRenderingStatus.Ready(NoesisRenderBackendKind.WindowsOpenGlReadback, detail);

        Assert.Equal(detail, status.Detail);
        Assert.Equal("Windows offscreen OpenGL selected (CPU readback). " + detail, status.ToString());
    }

    [Fact]
    public void Unavailable_ExposesReasonWithoutClaimingABackend()
    {
        const string detail = "No render backend is available.";

        NoesisRenderingStatus status = NoesisRenderingStatus.Unavailable(detail);

        Assert.False(status.IsReady);
        Assert.False(status.IsZeroCopy);
        Assert.Equal(NoesisRenderBackendKind.None, status.Backend);
        Assert.Equal(NoesisRenderTransferMode.None, status.TransferMode);
        Assert.Equal("Unavailable", status.BackendName);
        Assert.Equal(detail, status.Detail);
        Assert.Equal("Unavailable: " + detail, status.ToString());
    }

    [Fact]
    public void NotInitialized_IsAStableNotReadyStatus()
    {
        NoesisRenderingStatus status = NoesisRenderingStatus.NotInitialized;

        Assert.False(status.IsReady);
        Assert.False(status.IsZeroCopy);
        Assert.Equal(NoesisRenderBackendKind.None, status.Backend);
        Assert.Equal(NoesisRenderTransferMode.None, status.TransferMode);
        Assert.Equal("Not initialized", status.BackendName);
        Assert.Equal(string.Empty, status.Detail);
        Assert.Equal("Not initialized", status.ToString());
    }
}
