using System;
using System.Threading.Tasks;
using Godot;

// Smoke scripts belong to the plugin assembly even though their resources live under tests/.
// ReSharper disable once CheckNamespace
namespace NoesisGodot.Smoke;

/// <summary>
/// Opt-in engine smoke test for the complete NoesisView lifecycle. The process exit code makes it suitable for
/// platform runners once Godot and the native Noesis runtime are provisioned there.
/// </summary>
public partial class SmokeRunner : Node
{
    private static readonly Vector2I InitialSize = new(320, 180);
    private static readonly Vector2I ResizedSize = new(640, 360);

    public override async void _Ready()
    {
        try
        {
            await RunAsync();
            GD.Print("[NoesisGUI Smoke] PASS");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"[NoesisGUI Smoke] FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task RunAsync()
    {
        var view = new NoesisView
        {
            Name = "SmokeView",
            Xaml = "res://tests/Smoke/UI/Smoke.xaml",
            AlwaysRender = false,
            Size = InitialSize,
        };

        AddChild(view);
        await WaitForFrames(2);

        Require(view.View != null, "Noesis view was not created.");
        Require(view.Root != null, "XAML root was not loaded.");
        Require(view.Texture != null, "Initial frame did not produce a Godot texture.");
        RequireTextureSize(view, InitialSize, "initial render");
        RequireReadyRenderingStatus(view, "initial render");
        NoesisRenderBackendKind selectedBackend = view.RenderingStatus.Backend;
        NoesisRenderTransferMode selectedTransferMode = view.RenderingStatus.TransferMode;

        view.Size = ResizedSize;
        view.RequestRedraw();
        await WaitForFrames(2);
        RequireTextureSize(view, ResizedSize, "resize");
        RequireRenderingStatusUnchanged(view, selectedBackend, selectedTransferMode, "resize");

        var previousView = view.View;
        var previousRoot = view.Root;
        view.ReloadXaml();
        await WaitForFrames(2);

        Require(view.View != null, "Reload discarded the active view.");
        Require(view.Root != null, "Reload discarded the XAML root.");
        Require(!ReferenceEquals(previousView, view.View), "Reload did not replace the Noesis view.");
        Require(!ReferenceEquals(previousRoot, view.Root), "Reload did not replace the XAML root.");
        RequireTextureSize(view, ResizedSize, "reload");
        RequireRenderingStatusUnchanged(view, selectedBackend, selectedTransferMode, "reload");

        RemoveChild(view);
        Require(view.View == null, "Removing the node did not dispose its Noesis view.");
        Require(view.Root == null, "Removing the node did not release its XAML root.");
        Require(view.Texture == null, "Removing the node did not release its render texture.");
        Require(!view.RenderingStatus.IsReady, "Removing the node left its rendering status ready.");
        Require(view.RenderingStatus.Backend == NoesisRenderBackendKind.None, "Removing the node retained its selected backend.");
        Require(view.RenderingStatus.TransferMode == NoesisRenderTransferMode.None, "Removing the node retained its transfer mode.");

        view.Free();
        Require(!IsInstanceValid(view), "Godot node was not freed.");
    }

    private async Task WaitForFrames(int count)
    {
        for (var index = 0; index < count; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void RequireTextureSize(NoesisView view, Vector2I expected, string operation)
    {
        Require(view.Texture != null, $"{operation} did not produce a texture.");
        Require(view.Texture.GetSize() == expected, $"{operation} produced {view.Texture.GetSize()}, expected {expected}.");
    }

    private static void RequireReadyRenderingStatus(NoesisView view, string operation)
    {
        NoesisRenderingStatus status = view.RenderingStatus;
        Require(status.IsReady, $"{operation} did not report a ready rendering status: {status}");
        Require(status.Backend != NoesisRenderBackendKind.None, $"{operation} did not report a render backend.");
        Require(status.TransferMode != NoesisRenderTransferMode.None, $"{operation} did not report a transfer mode.");
        Require(status.IsZeroCopy == (status.TransferMode == NoesisRenderTransferMode.ZeroCopy), $"{operation} reported inconsistent zero-copy state.");
        Require(!string.IsNullOrWhiteSpace(status.BackendName), $"{operation} did not report a backend name.");
        Require(!string.IsNullOrWhiteSpace(status.ToString()), $"{operation} produced an empty rendering summary.");
    }

    private static void RequireRenderingStatusUnchanged(NoesisView view, NoesisRenderBackendKind expectedBackend, NoesisRenderTransferMode expectedTransferMode, string operation)
    {
        RequireReadyRenderingStatus(view, operation);
        Require(view.RenderingStatus.Backend == expectedBackend, $"{operation} changed the selected render backend.");
        Require(view.RenderingStatus.TransferMode == expectedTransferMode, $"{operation} changed the render transfer mode.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
