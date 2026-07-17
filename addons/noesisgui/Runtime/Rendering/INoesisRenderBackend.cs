using System;

namespace NoesisGodot;

/// <summary>
/// A rendering strategy for a Noesis view. Two implementations:
///  - OffscreenGLBackend: private GL context + CPU readback (works everywhere)
///  - SharedGLBackend: GL context shared with Godot's, renders directly into a Godot-owned texture (zero-copy; Compatibility renderer only)
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
