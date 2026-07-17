using System.IO;

namespace NoesisGodot;

/// <summary>
/// Serves images referenced from XAML (e.g. &lt;Image Source="Images/logo.png"/&gt;) out of res://.
/// FileTextureProvider handles decoding (png/jpg/dds/ktx...) and GPU upload through the active RenderDevice;
/// we only supply the byte stream.
///
/// IMPORTANT: Godot imports .png/.jpg into .ctex and may strip the originals from the export. Keep Noesis-referenced
/// images as raw files, e.g. add their folder to export "Include" filters (*.png) or use the .keep trick. See README.
/// </summary>
public class GodotTextureProvider : Noesis.FileTextureProvider
{
    public override Stream OpenStream(System.Uri uri)
    {
        string resPath = GodotResourceUtil.ToResPath(uri);
        return GodotResourceUtil.OpenRead(resPath, "Texture");
    }
}
