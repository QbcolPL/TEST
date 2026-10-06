# Windows Builder 2.3.0 — local build

## Requirements

- Windows x64
- .NET 10 SDK x64
- internet access for NuGet restore

Check:

```cmd
dotnet --list-sdks
```

You need an SDK entry beginning with `10.`.

Install with WinGet when needed:

```cmd
winget install Microsoft.DotNet.SDK.10 --source winget
```

## Build

From the repository root, double-click:

```text
BUILD_WINDOWS_BUILDER.cmd
```

or run:

```text
tools\BUILD_WINDOWS_BUILDER_LOCAL.cmd
```

The script performs:

1. repository detection,
2. `.NET` command check,
3. `.NET SDK 10.x` check,
4. project check,
5. NuGet restore,
6. self-contained `win-x64` publish,
7. EXE verification.

Output:

```text
artifacts\windows\FieldTakHub.Builder\FieldTakHub.Builder.exe
```

## Meshtastic role mapping

The Builder keeps the user-facing label:

```text
FIELD NODE
```

but writes the native Meshtastic role:

```text
tak
```

Legacy `field_node` is accepted when loading old projects and is normalized to `tak`.
`gateway` remains a separate role.
