using System;

namespace NoesisGodot;

/// <summary>
/// A rendering strategy for a Noesis view. Implementations, in selection order:
///  - SharedGLBackend: zero-copy via GL context sharing (Windows, Compatibility renderer)
///  - VkSharedGLBackend: zero-copy via Vulkan external memory (Windows, Forward+/Mobile;
///    requires VK_KHR_external_memory_win32 on Godot's device — engine-gated today)
///  - OffscreenGLBackend: private WGL context + CPU readback (Windows fallback)
///  - EglOffscreenBackend: headless EGL pbuffer + CPU readback (Linux, all renderers)
/// </summary>
internal interface INoesisRenderBackend : IDisposable
{
    Noesis.RenderDevice Device { get; }

    /// <summary>True, if the output texture is vertically flipped (GPU-side render targets are bottom-up in GL), the displaying node compensates.</summary>
    bool OutputIsFlipped { get; }

    void Init(int width, int height);

    void Resize(int width, int height);

    /// <summary>Make this backend's GL context current (renderer init/shutdown).</summary>
    void BeginContext();

    /// <summary>Restore the caller's GL context.</summary>
    void EndContext();

    /// <summary>Ticks and renders the view; returns the output texture (instance may change on resize) or null if not ready.</summary>
    Godot.Texture2D RenderFrame(Noesis.View view, double timeSeconds);
}
