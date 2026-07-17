# NoesisGodotNet

Host [NoesisGUI](https://www.noesisengine.com/) XAML views inside Godot 4 with true WPF-style MVVM in C#. Design UI in Noesis Studio / Blend, bind it to plain `INotifyPropertyChanged` ViewModels, and composite it like any Godot Control.

> Unofficial community integration and is not affiliated with or endorsed by Noesis Technologies (yet?).

There is no official Noesis <-> Godot integration anywhere; this is the first. Verified with Godot 4.7.1 .NET (Forward+/Vulkan) and Noesis 3.2.13: XAML loading, MVVM data binding, commands, styled/templated controls, mouse + keyboard input, and clean shutdown all functional.

## Requirements

- Godot 4+ **.NET** build (tested on 4.7.1)
- .NET 8 SDK
- A NoesisGUI license - see [pricing](https://www.noesisengine.com/licensing.php)
- Windows (the current render backend uses WGL; see roadmap)

## Installing in your game

The addon is pure C# source; the Noesis runtime (managed + native) comes from the public [`Noesis.GUI` NuGet package](https://www.nuget.org/packages/Noesis.GUI). No SDK download or Noesis account needed to build.

1. Copy the `addons/noesisgui/` folder into your Godot .NET project. _Godot compiles it as part of your game assembly automatically._
2. Add to your game's `.csproj`:

   ```xml
   <ItemGroup>
     <PackageReference Include="Noesis.GUI" Version="3.2.*" />
   </ItemGroup>
   ```

   and inside the main `<PropertyGroup>`:

   ```xml
   <!-- Noesis.GUI ships native assets under legacy RIDs (win10-x64);
        without this .NET 8+ silently skips them (NETSDK1206). -->
   <UseRidGraph>true</UseRidGraph>
   ```

3. Build once, then enable **NoesisGodotNet** in **Project Settings → Plugins**.
4. Set your license in **Project Settings → NoesisGUI** (`noesis_gui/license/name`, `noesis_gui/license/key`), or via `NOESIS_LICENSE_NAME` / `NOESIS_LICENSE_KEY` environment variables. Point `noesis_gui/resources/root` at the folder holding your XAML.
5. Add a `NoesisView` node and set its `Xaml` property. Done.

## Running this repo

This repo is itself a Godot project with a working example: open in Godot 4.x .NET (restores NuGet on build), set a license (or run in eval mode), and run `examples/HelloNoesis/Main.tscn`.

## Usage

Add a `NoesisView` node (extends `TextureRect`), set its `Xaml` property (relative to `noesis_gui/resources/root`, or an absolute `res://` path), and hand it a ViewModel:

```csharp
GetNode<NoesisView>("NoesisView").ViewModel = new MainMenuViewModel();
```

The ViewModel is plain .NET. `INotifyPropertyChanged`, `ICommand`, zero engine types - so the same UI + logic previews in Noesis Studio and unit-tests outside Godot.

### Theme

The official Noesis theme loads by default (embedded in the `Noesis.App.Theme` package), so every standard control - TextBox, Slider, ComboBox, ScrollViewer, ProgressBar has proper styles out of the box. `noesis_gui/theme/xaml` controls it: the default `Theme/NoesisTheme.DarkBlue.xaml`, another variant like `Theme/NoesisTheme.LightBlue.xaml`, a path to your own ResourceDictionary (relative to the resources root or `res://`), or empty to opt out (then untemplated controls render as Noesis's pink fallback). See `examples/ThemeShowcase/`.

### World-space UI

`NoesisView3D` puts XAML on a quad in 3D - in-world screens, holo-panels, terminals. Set `Xaml`, `PanelSize` (world units), and `PixelsPerMeter` (texture resolution). Mouse input is raycast from the camera and mapped into the view; the last-clicked panel owns keyboard focus. Hot-reload and ViewModels work exactly like the 2D `NoesisView` (both wrap the same `NoesisViewHost`). See `examples/WorldSpace/`.

**Sizing world-space UI.** How large a panel appears is entirely a camera/placement decision - the node only controls physical size (`PanelSize`) and texture density (`PixelsPerMeter`). Two rules of thumb:

- *Apparent size*: a panel fills the screen vertically when `PanelSize.y ≈ 2 × distance × tan(fov/2)`. The example uses a 0.9m-tall panel at 0.9m with a 60° FOV (~87% of screen height).
- *Crispness vs. cost*: pick `PixelsPerMeter` so the texture roughly matches the panel's on-screen pixel size at typical viewing distance. Too low looks soft; too high wastes render + readback bandwidth every frame. The XAML's own layout matters too - a fixed-width column designed for fullscreen 2D will occupy only part of a wide panel (wrap content in a `Viewbox` if you want it to scale to fill instead).

### Editor & UX niceties

- **Broken-XAML overlay**: if a hot-reload save has invalid markup, the running view keeps the last good frame and shows the parse error in an overlay strip; it clears on the next successful save.
- **Cursor forwarding**: the OS cursor follows the UI (I-beam over text boxes, hand over hyperlinks) - in 3D, scoped to while the panel is hovered.
- **Gamepad**: joypad buttons map to Noesis gamepad navigation keys (D-pad focus movement, A/B accept/cancel, shoulder page) on the focused view/panel.
- **Project → Tools → Open Selected XAML in Noesis Studio**: opens the FileSystem-dock selection in Studio. Set `noesis_gui/editor/studio_path` to the Studio executable, or leave empty to use the OS `.xaml` association.

### XAML hot-reload

When running from the editor, the plugin watches `noesis_gui/resources/root` for `.xaml` changes. Save a file in Noesis Studio, Rider, anywhere, and the running game updates live: resource dictionaries and templates refresh via Noesis's reload mechanism, and any `NoesisView` whose root document changed is rebuilt in place with its ViewModel preserved. Invalid markup mid-edit is tolerated (the last good view stays up with a warning). Exported builds skip all of this.

### Asset conventions

- XAML, fonts (`.ttf`/`.otf`), and XAML-referenced images live under `noesis_gui/resources/root`.
- `.xaml` files are handled by the bundled import plugin: visible in the FileSystem dock and automatically included in exports (as `XamlFile` resources). No export filters are needed.
- XAML-referenced **images and fonts** still need to ship raw: add them to your export preset's *non-resource include filter* (e.g. `*.ttf, UI/Images/*`), since Godot's importer would otherwise only export the converted `.ctex` versions.
- Fonts: reference by family WPF-style - `FontFamily="./#Orbitron"`.

## How it works

Godot `_Process` → Noesis `View.Update` → render on a private offscreen GL context (`RenderDeviceGL`) → readback → `ImageTexture` shown by the `NoesisView` control (premultiplied-alpha blend). Input events from `_GuiInput` are translated 1:1 (mouse, keyboard incl. text input, wheel, touch).

The offscreen-context design works identically under **Forward+ (Vulkan)** and **Compatibility (GL)** and can never corrupt Godot's render state. The cost is a GPU→CPU copy per frame which is perfectly fine for menus/HUDs; zero-copy paths are the top roadmap item.

## Rendering backends

The plugin picks the fastest backend at startup, per view:

- **Zero-copy (Compatibility/GL renderer)**: a second GL context shared with Godot's renders Noesis directly into a Godot-owned texture via FBO — no per-frame CPU copy. Requires single-threaded Compatibility rendering (the default). Disable with `noesis_gui/rendering/zero_copy` if needed.
- **Readback (everything else)**: private GL context + `glReadPixels` + texture upload. Works under Forward+/Mobile (Vulkan) and threaded GL. Fine for menus/HUDs.

The startup log prints which backend each view got.

## Roadmap

0. ~~Theme integration~~ (done in 0.4)
1. ~~Zero-copy Compatibility path~~ (done in 0.7)
2. Vulkan interop (external memory / D3D11 shared handles) to kill the readback on Forward+
3. Editor QoL: XAML preview in the editor (hot-reload + import: 0.2; error overlay, cursor forwarding, gamepad, Studio button: 0.6)
4. ~~World-space UI~~ (done in 0.5: `NoesisView3D`)
5. Linux/macOS render backends (EGL/GLX/NSGL variants of the WGL pattern)
6. C++ GDExtension core for GDScript users (same architecture, native)

## License notes

This plugin code is yours to license as you wish. NoesisGUI itself is commercial software — your Noesis license governs shipping it. Do not commit license keys; use env vars or an untracked `override.cfg`.
