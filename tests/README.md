# Tests

## Pure .NET unit tests

Run the resource-path and input-mapping contracts without starting Godot:

```shell
dotnet test tests/NoesisGodot.Tests/NoesisGodot.Tests.csproj -c Debug
```

## Godot lifecycle smoke test

`Smoke/SmokeRunner.tscn` creates a real `NoesisView`, renders it, resizes it, reloads its XAML, and disposes it. A passing run prints `[NoesisGUI Smoke] PASS` and exits with code `0`; a failure exits with code `1`.

Build the C# assembly, then run the scene from the repository root:

```shell
dotnet build NoesisGodot.csproj -c Debug
godot --path . res://tests/Smoke/SmokeRunner.tscn
```

The smoke test needs the platform's native Noesis runtime and a working GL/EGL context. `--headless` can be added on runners that provide a headless-capable graphics context; it is intentionally not part of the pure .NET CI job.

The runner is compiled only in the normal `Debug` configuration and is excluded from `ExportDebug` and `ExportRelease` assemblies.
