# Changelog

## 0.10.1 - Test foundation

- Added a standalone xUnit suite for `res://` path normalization and
  Godot-to-Noesis keyboard/gamepad mappings
- Added an opt-in Godot lifecycle smoke scene covering view creation,
  rendering, resize, XAML reload, and disposal
- Fixed `NoesisView` retaining its last render texture after leaving the scene tree
- CI now builds the solution and runs the pure .NET tests on Windows and
  Linux; smoke-test code remains Debug-only and is excluded from export
  assemblies
- Tag releases now publish the matching changelog section and fail when the
  tag, plugin version, or changelog entry disagree

## 0.10.0 - Stability hardening

- Made view initialization, XAML reload, renderer shutdown, and backend
  disposal transactional and failure-safe
- Preserved activation and the last valid view across hot reloads, with safer
  watcher and callback cleanup
- Hardened `res://` URI normalization so absolute Godot resource paths keep
  their full path and casing
- Removed the extra CPU row-flip copy from Windows and Linux readback backends
  and tightened GL/EGL context, resize, and cleanup handling
- Improved world-space input capture, cursor reset, edge clamping, and
  teardown behavior

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
