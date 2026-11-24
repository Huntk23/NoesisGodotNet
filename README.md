# NoesisGodotNet

Host [NoesisGUI](https://www.noesisengine.com/) XAML views in Godot 4 .NET. Build UI in Noesis Studio or Blend, bind it
to ordinary C# view models, and use it as screen-space or world-space Godot UI.

> Independent, unofficial community integration,
> [endorsed by Noesis Technologies in its developer/community forum](https://www.noesisengine.com/forums/viewtopic.php?p=545#p545).
> It is not developed by or affiliated with Noesis Technologies.

The plugin supports MVVM binding and commands, styled controls, mouse, keyboard, touch and gamepad input, XAML
hot-reload, 2D controls, and 3D panels. The repository currently targets Godot 4.7.1 and NoesisGUI 3.2.

## Requirements and platforms

- Godot 4 .NET
- .NET 10 SDK
- An IDE that supports .NET 10, such as Visual Studio 2026 or a current JetBrains Rider (Visual Studio 2022 cannot open
  `net10.0` projects)
- A [NoesisGUI license](https://www.noesisengine.com/licensing.php) for licensed use

| Platform | Rendering path                                                           | Status            |
|----------|--------------------------------------------------------------------------|-------------------|
| Windows  | Shared OpenGL zero-copy, engine-gated Vulkan interop, or OpenGL readback | Supported         |
| Linux    | EGL/OpenGL readback on X11 and Wayland                                   | Supported         |
| macOS    | Compatibility backend planned                                            | Not yet supported |

This repository targets `net10.0`. .NET 8 reached end of support in November 2026, so new projects should target
`net10.0` as well. The addon is compiled into your own project, so Godot 4.7 projects that still target `net8.0` keep
working; CI checks that the addon builds for `net8.0`. Godot 4.8 and later require `net10.0`.

## Install

1. Copy `addons/noesisgui/` into your Godot .NET project.
2. In your project file, set `TargetFramework` to `net10.0` (recommended) and add the packages and legacy RID support:

   ```xml
   <PropertyGroup>
     <TargetFramework>net10.0</TargetFramework>
     <UseRidGraph>true</UseRidGraph>
   </PropertyGroup>

   <ItemGroup>
     <PackageReference Include="Noesis.GUI" Version="3.2.*" />
     <PackageReference Include="Noesis.App.Theme" Version="3.2.*" />
   </ItemGroup>
   ```

3. Build once, then enable **NoesisGodotNet** under **Project Settings > Plugins**.
4. Configure `noesis_gui/license/name` and `noesis_gui/license/key`, or use the `NOESIS_LICENSE_NAME` and
   `NOESIS_LICENSE_KEY` environment variables.
5. Set `noesis_gui/resources/root` to your UI folder, add a `NoesisView`, and assign its `Xaml` property.

Do not commit license keys. Environment variables or an untracked `override.cfg` are safer.

## Quick start

`NoesisView` is a Godot `TextureRect`. Assign any CLR object as its WPF-style `DataContext`:

```csharp
NoesisView view = GetNode<NoesisView>("NoesisView");
view.ViewModel = new MainMenuViewModel();
```

View models can use `INotifyPropertyChanged` and `ICommand` without depending on Godot types, so the same UI logic can
run in Noesis Studio and ordinary .NET tests.

The default theme is `Theme/NoesisTheme.DarkBlue.xaml`. Change `noesis_gui/theme/xaml` to another embedded theme, your
own resource dictionary, or an empty value to disable it.

## Features and examples

- [HelloNoesis](examples/HelloNoesis/) - basic XAML, MVVM binding, and commands
- [ThemeShowcase](examples/ThemeShowcase/) - standard controls and theme variants
- [WorldSpace](examples/WorldSpace/) - `NoesisView3D` panels with ray-picked input
- [Hardening](examples/Hardening/) - interaction and lifecycle exercise scene

Editor runs watch the resource root for XAML changes. Successful saves rebuild affected views while preserving their
view models; invalid saves keep the last valid view and display the parse error. Exported builds do not run the watcher.

## Assets and exports

- Keep XAML, fonts, and XAML-referenced images under `noesis_gui/resources/root`.
- The importer includes `.xaml` resources in exports automatically.
- Add raw fonts and images to the export preset's **non-resource include filter** because Godot would otherwise ship
  only its imported formats. For example: `*.ttf,UI/Images/*`.
- Reference fonts by family, for example `FontFamily="./#Orbitron"`.

## Rendering

Each view asks a backend factory for the fastest supported path:

1. Windows shared OpenGL when Godot Compatibility has a current, single-threaded GL context.
2. Windows Vulkan/OpenGL external-memory interop when Godot exposes the required device extension.
3. A platform readback backend: WGL on Windows or EGL on Linux.

The Vulkan path remains inactive on stock Godot until the engine exposes the required external-memory capability; see
[godot-proposals #15210](https://github.com/godotengine/godot-proposals/issues/15210). All zero-copy paths are
controlled by `noesis_gui/rendering/zero_copy` and fall back automatically.

The selected backend is logged once per view and is available to game diagnostics:

```csharp
GD.Print(view.RenderingStatus);
bool zeroCopy = view.RenderingStatus.IsZeroCopy;
```

Readback uses a private GL context and uploads an `ImageTexture`, keeping Godot render state isolated. It works under
both Forward+/Mobile and Compatibility renderers, at the cost of a GPU-to-CPU copy each rendered frame.

## Run this repository

Open the repository in Godot 4.7.1 .NET, build it, configure a license or evaluation mode, and run
`examples/HelloNoesis/Main.tscn`.

Run the standalone tests with:

```powershell
dotnet test NoesisGodot.sln
```

See [CHANGELOG.md](CHANGELOG.md) for release history.

## Roadmap

- macOS compatibility/readback backend and platform validation
- Native Metal rendering after Godot's texture-import path is production-ready
- Additional Vulkan external-memory paths as Godot exposes the required APIs
- Optional GDExtension core for non-.NET projects

## Licensing

The plugin is MIT licensed. NoesisGUI is commercial software and remains governed by your Noesis license.
