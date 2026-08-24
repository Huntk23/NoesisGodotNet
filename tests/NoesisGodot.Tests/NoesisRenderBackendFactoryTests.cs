using NoesisGodot;

namespace NoesisGodot.Tests;

public sealed class NoesisRenderBackendFactoryTests
{
    public static TheoryData<int, bool, bool, bool, NoesisRenderBackendKind[]> CandidateCases => new()
    {
        {
            (int) NoesisHostPlatform.Windows, true, true, false,
            [NoesisRenderBackendKind.SharedOpenGl, NoesisRenderBackendKind.WindowsOpenGlReadback]
        },
        {
            (int) NoesisHostPlatform.Windows, true, false, true,
            [NoesisRenderBackendKind.VulkanOpenGlInterop, NoesisRenderBackendKind.WindowsOpenGlReadback]
        },
        {
            (int) NoesisHostPlatform.Windows, true, true, true,
            [
                NoesisRenderBackendKind.SharedOpenGl,
                NoesisRenderBackendKind.VulkanOpenGlInterop,
                NoesisRenderBackendKind.WindowsOpenGlReadback,
            ]
        },
        {
            (int) NoesisHostPlatform.Windows, true, false, false,
            [NoesisRenderBackendKind.WindowsOpenGlReadback]
        },
        {
            (int) NoesisHostPlatform.Windows, false, true, true,
            [NoesisRenderBackendKind.WindowsOpenGlReadback]
        },
        {
            (int) NoesisHostPlatform.Linux, true, true, true,
            [NoesisRenderBackendKind.LinuxEglReadback]
        },
        {
            (int) NoesisHostPlatform.MacOs, true, true, true,
            []
        },
        {
            (int) NoesisHostPlatform.Unsupported, true, true, true,
            []
        },
    };

    [Theory]
    [MemberData(nameof(CandidateCases))]
    public void GetCandidateOrder_ReturnsDeterministicFallbackOrder(int platformValue, bool zeroCopyEnabled,
        bool sharedOpenGlSupported, bool vulkanInteropSupported, NoesisRenderBackendKind[] expected)
    {
        IReadOnlyList<NoesisRenderBackendKind> actual = NoesisRenderBackendFactory.GetCandidateOrder(
            (NoesisHostPlatform) platformValue, zeroCopyEnabled, sharedOpenGlSupported, vulkanInteropSupported);

        Assert.Equal(expected, actual);
    }
}
