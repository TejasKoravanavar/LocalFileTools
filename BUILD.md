# Building LocalFileTools

Install the .NET 10 SDK and Inno Setup 7 on Windows. Run `Build-Release.ps1` from PowerShell, passing the full path to `ISCC.exe` if it is not on `PATH`:

```powershell
.\Build-Release.ps1 -InnoCompilerPath 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
```

The script publishes x64 and x86 single-file, framework-dependent executables, packages their native dependencies, and builds `LocalFileTools-Setup.exe`. A clean GitHub repository should include the source files and installer script; attach the compiled Setup EXE to a GitHub Release.

To run the feature checks on an x64 development computer:

```powershell
dotnet run --project .\Tests\LocalFileTools.Tests.csproj -c Release -r win-x64
```

The setup script downloads the official Microsoft .NET runtime only when the destination computer needs it. Those runtime ZIP URLs and SHA-256 values are pinned in `Installer.iss`.
