# Changelog

## 0.9.2

- Release infrastructure: MIT LICENSE, this changelog, CI build checks
  (Windows + Linux), tag-triggered addon packaging
- Vulkan interop: capability is probed before any GL/Noesis objects are
  created; unsupported devices log one informative line instead of a
  per-view warning (and no longer risk side effects on the fallback backend)
- Fixed theme font loading: embedded font manifest names are recorded at
  enumeration instead of reconstructed (PT Root UI now actually loads -
  previously silently substituted by system fonts since 0.4)
- Platform-aware font fallbacks (Noto/DejaVu/Liberation on Linux)
- Documentation sync

## 0.9-0.9.1 - Vulkan zero-copy interop (engine-gated)

- `VkSharedGLBackend` + `VulkanInterop`: allocates an exportable VkImage on
  Godot's own Vulkan device, imports the memory into a private GL context
  (`GL_EXT_memory_object_win32`), Noesis renders into it, Godot samples it via
  `TextureCreateFromExtension` + `Texture2DRD` - zero copies
- Currently inactive on stock Godot: the device lacks
  `VK_KHR_external_memory_win32` (see docs/godot-proposal-external-memory.md);
  automatic fallback to readback

## 0.8 - Linux support

- `EglOffscreenBackend`: headless EGL pbuffer readback, works under every
  Godot renderer on X11 and Wayland (dual EGL/GLX context save-restore)
- Platform-aware backend selection; clear error on unsupported platforms

## 0.7 - Zero-copy rendering (Compatibility)

- `INoesisRenderBackend` abstraction
- `SharedGLBackend`: GL context shared with Godot's via wglShareLists renders
  straight into a Godot-owned texture (no per-frame CPU copy)
- Automatic fallback to readback (Forward+, threaded GL, init failure)

## 0.6 - Editor & UX polish

- On-screen overlay for invalid XAML during hot-reload
- Mouse cursor forwarding (I-beam over text, hand over links; hover-scoped in 3D)
- Gamepad navigation (D-pad focus, accept/cancel, paging)
- Tools menu: "Open Selected XAML in Noesis Studio"
- Hot-reload detects Noesis's lenient parser errors via the log channel

## 0.5 - World-space UI

- `NoesisViewHost`: shared view lifecycle core
- `NoesisView3D`: XAML on 3D quads with ray-picked mouse input and
  keyboard-focus ownership

## 0.4 - Official theme

- `Noesis.App.Theme` integration: embedded theme XAML + fonts resolved
  through the providers; `noesis_gui/theme/xaml` setting
- Themed showcase example (TextBox, Slider, ComboBox, ScrollViewer, ProgressBar)

## 0.3 - Hardening

- View activation wired to focus (caret, selection visuals)
- Container-friendly sizing (ExpandMode fix)
- Hot-reload parse validation keeps the last good view on broken saves
- Verified: both renderers, export builds, input/focus/resize

## 0.2 - XAML dev workflow

- Runtime hot-reload (file watcher + in-place view rebuild, ViewModel preserved)
- `.xaml` import plugin: FileSystem dock visibility + automatic export inclusion

## 0.1 - First light

- First known NoesisGUI <-> Godot integration: XAML rendering, MVVM data
  binding, input forwarding, res:// resource providers, offscreen GL readback
