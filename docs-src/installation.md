# Installation

## Requirements

- **ArcGIS Pro 3.6** or later (the add-in manifest targets `desktopVersion="3.6"`).
- **ArcGIS Pro SDK for .NET** installed (adds the Visual Studio project templates and the
  `Esri.ProApp.SDK.Desktop.targets` build integration this project relies on).
- **Visual Studio 2022** with the .NET desktop development workload.

## Build from source

1. Clone the repository:

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
