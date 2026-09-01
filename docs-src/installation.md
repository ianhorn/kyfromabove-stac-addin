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

## Requirements

- **ArcGIS Pro 3.7** or later (the add-in manifest targets `desktopVersion="3.7.0.1901"` on this
  branch; see [Which branch do I need?](#which-branch-do-i-need) above).
- **ArcGIS Pro SDK for .NET** installed (adds the Visual Studio project templates and the
  `Esri.ProApp.SDK.Desktop.targets` build integration this project relies on).
- **Visual Studio 2026 ("18"), version 18.4.1 or later** -- this is what Esri certifies the Pro 3.7
  SDK against.

## Build from source

1. Clone the repository (this branch, `master`, targets ArcGIS Pro 3.7+; see above if you have an
   earlier version):

    ```bash
    git clone https://github.com/ianhorn/kyfromabove-stac-addin.git
    ```

2. Open `kyfromabove-ext.sln` (or `kyfromabove-ext.slnx`) in Visual Studio.
3. Build the `KyFromAboveSTACAddin` project (Debug or Release). The ArcGIS Pro SDK's build targets
   package the compiled assembly, `Config.daml`, and the toolbar images into an `.esriAddinX` file
   and register it with ArcGIS Pro automatically.
4. Press **F5** (or **Start**) to launch ArcGIS Pro with the add-in already loaded, or just open
   ArcGIS Pro normally -- once built, the add-in stays registered.

!!! tip "No Visual Studio?"
    You only need the compiled `.esriAddinX` file to *use* the add-in. Copy it to
    `%LocalAppData%\ESRI\ArcGISPro\AssemblyCache` (or double-click it) and ArcGIS Pro will install
    it through its normal Add-In Manager flow.

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
