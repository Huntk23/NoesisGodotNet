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
4. Set your license in **Project Settings → NoesisGUI** (`noesisgui/license/name`, `noesisgui/license/key`), or via `NOESIS_LICENSE_NAME` / `NOESIS_LICENSE_KEY` environment variables. Point `noesisgui/resources/root` at the folder holding your XAML.
5. Add a `NoesisView` node and set its `Xaml` property. Done.

## Running this repo

This repo is itself a Godot project with a working example: open in Godot 4.x .NET (restores NuGet on build), set a license (or run in eval mode), and run `examples/HelloNoesis/Main.tscn`.

## Usage

Add a `NoesisView` node (extends `TextureRect`), set its `Xaml` property (relative to `noesisgui/resources/root`, or an absolute `res://` path), and hand it a ViewModel:

```csharp
GetNode<NoesisView>("NoesisView").ViewModel = new MainMenuViewModel();
```

The ViewModel is plain .NET. `INotifyPropertyChanged`, `ICommand`, zero engine types - so the same UI + logic previews in Noesis Studio and unit-tests outside Godot.

### Asset conventions

- XAML, fonts (`.ttf`/`.otf`), and XAML-referenced images live under `noesisgui/resources/root`.
- Keep XAML-referenced images as **raw files**: Godot's importer converts png/jpg to `.ctex` and can omit originals from exports. Add `*.xaml`, the images, and fonts to your export preset's *non-resource include filter* (e.g. `*.xaml, *.ttf, UI/Images/*`).
- Fonts: reference by family WPF-style - `FontFamily="./#Orbitron"`.

## How it works

Godot `_Process` → Noesis `View.Update` → render on a private offscreen GL context (`RenderDeviceGL`) → readback → `ImageTexture` shown by the `NoesisView` control (premultiplied-alpha blend). Input events from `_GuiInput` are translated 1:1 (mouse, keyboard incl. text input, wheel, touch).

The offscreen-context design works identically under **Forward+ (Vulkan)** and **Compatibility (GL)** and can never corrupt Godot's render state. The cost is a GPU→CPU copy per frame which is perfectly fine for menus/HUDs; zero-copy paths are the top roadmap item. Details and trade-offs: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Roadmap

1. Zero-copy Compatibility path (share Godot's GL context, render into an FBO-backed Godot texture)
2. Vulkan interop (external memory / D3D11 shared handles) to kill the readback on Forward+
3. Editor QoL: `.xaml` import plugin + inspector preview, XAML hot-reload, Noesis Studio "open in" button
4. World-space UI helper (XAML on a quad in 3D)
5. Linux/macOS render backends (EGL/GLX/NSGL variants of the WGL pattern)
6. C++ GDExtension core for GDScript users (same architecture, native)

## License notes

This plugin code is yours to license as you wish. NoesisGUI itself is commercial software — your Noesis license governs shipping it. Do not commit license keys; use env vars or an untracked `override.cfg`.
