# Installation

## Which branch do I need?

This add-in targets different ArcGIS Pro releases on different branches, because Esri ties each
Pro release to a specific .NET version and Visual Studio requirement:

| You have               | Branch        | Target framework | Visual Studio                | `Config.daml` `desktopVersion` |
|-------------------------|---------------|-------------------|-------------------------------|----------------------------------|
| ArcGIS Pro 3.6.x         | `for-v3.6.x`  | `net8.0-windows`  | VS 2022, 17.13+                | `3.6`                            |
| ArcGIS Pro 3.7 or later  | `master`      | `net10.0-windows` | VS 2026 ("18"), 18.4.1+        | `3.7.0.1901`                     |

Check out the branch that matches your installed Pro version *before* building:

```bash
git checkout for-v3.6.x   # for ArcGIS Pro 3.6.x
# or
git checkout master       # for ArcGIS Pro 3.7+
```

The two branches reference ArcGIS Pro's assemblies by direct file path
(`C:\Program Files\ArcGIS\Pro\bin\...`), not a versioned NuGet package, so whichever Pro version is
actually installed on your machine is what gets compiled against -- building `for-v3.6.x` on a
machine that only has Pro 3.7 installed (or vice versa) will fail with assembly version mismatch
errors (`CS1705`), not produce a working add-in for the version you wanted.

## Download the pre-built add-in

No Visual Studio or source build required -- download the `.esriAddinX` that matches your ArcGIS
Pro version, then double-click it (or copy it to `%LocalAppData%\ESRI\ArcGISPro\AssemblyCache`) to
install it through ArcGIS Pro's normal Add-In Manager flow:

- [Download for ArcGIS Pro 3.6.x](https://github.com/ianhorn/kyfromabove-stac-addin/releases/latest/download/KyFromAboveSTACAddin-3.6.x.esriAddinX)
- [Download for ArcGIS Pro 3.7 or later](https://github.com/ianhorn/kyfromabove-stac-addin/releases/latest/download/KyFromAboveSTACAddin-3.7.x.esriAddinX)

These links always point at the latest [GitHub Release](https://github.com/ianhorn/kyfromabove-stac-addin/releases),
rebuilt from the tip of each branch whenever a new release is cut (see `tools/release.ps1`); see
[Build from source](#build-from-source) below if you'd rather build it yourself.

## Requirements

- **ArcGIS Pro 3.6** or later (the add-in manifest targets `desktopVersion="3.6"` on this branch;
  see [Which branch do I need?](#which-branch-do-i-need) above).
- **ArcGIS Pro SDK for .NET** installed (adds the Visual Studio project templates and the
  `Esri.ProApp.SDK.Desktop.targets` build integration this project relies on).
- **Visual Studio 2022, version 17.13 or later** -- this is what Esri certifies the Pro 3.6 SDK
  against. Visual Studio 2026 (Community 18.9+) also builds this branch successfully in practice,
  if that's what you already have installed for the `master` branch's Pro 3.7+ SDK.

## Build from source

1. Clone the repository and check out the branch matching your Pro version (see above):

    ```bash
    git clone https://github.com/ianhorn/kyfromabove-stac-addin.git
    cd kyfromabove-stac-addin
    git checkout for-v3.6.x
    ```

2. Open `kyfromabove-ext.sln` in Visual Studio. Use the classic `.sln`, not `kyfromabove-ext.slnx`
   -- the `.slnx` format only became stable by default in VS 17.14, and 17.13 (this branch's
   minimum) only supports it as an opt-in preview feature.
3. Build the `KyFromAboveSTACAddin` project (Debug or Release). The ArcGIS Pro SDK's build targets
   package the compiled assembly, `Config.daml`, and the toolbar images into an `.esriAddinX` file
   and register it with ArcGIS Pro automatically.
4. Press **F5** (or **Start**) to launch ArcGIS Pro with the add-in already loaded, or just open
   ArcGIS Pro normally -- once built, the add-in stays registered.

!!! tip "No Visual Studio?"
    You only need the compiled `.esriAddinX` file to *use* the add-in -- grab one from
    [Download the pre-built add-in](#download-the-pre-built-add-in) above instead of building from
    source.

## Code signing (optional)

The project can Authenticode-sign the compiled assembly before packaging. Run the one-time setup:

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup-code-signing.ps1
```

Signing is on by default; disable it for a single build with:

```bash
dotnet build -p:SignAddin=false
```

## Verifying the install

Open ArcGIS Pro and look for the **KyFromAbove-STAC-AddIn** ribbon tab with a **STAC Search** button. If
it's missing, confirm the build succeeded with no errors and that ArcGIS Pro was restarted after
the first build.
