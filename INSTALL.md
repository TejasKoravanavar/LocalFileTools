# Installing LocalFileTools

Run `LocalFileTools-Setup.exe` on Windows 10 or 11 and follow the prompts. Setup installs for the current user without administrator approval. It creates a Start menu shortcut and optionally adds a desktop shortcut and File Explorer entries. On Windows 11 the right-click command may be under **Show more options**. To pass several selected files, use **Send to > LocalFileTools**.

Setup is about 30 MB. If .NET 10 Desktop Runtime is absent, the first install needs internet to download roughly 70–76 MB from Microsoft. The installer checks pinned SHA-256 hashes. Windows 11 Arm64 may run the x64 build under emulation, but that hardware has not been tested.

Uninstall from Windows Settings > Apps > Installed apps. The app's private runtime, when installed, is removed with it. The unsigned installer may trigger a Windows SmartScreen warning.
