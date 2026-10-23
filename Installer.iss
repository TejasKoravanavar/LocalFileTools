#define AppName "LocalFileTools"
#define AppVersion "0.7.0"

[Setup]
AppId={{f822f121-24dc-4301-974d-17b3638742ef}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=LocalFileTools
DefaultDirName={userpf}\LocalFileTools-v6
DefaultGroupName=LocalFileTools
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x86
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2/ultra
SolidCompression=yes
ArchiveExtraction=full
OutputDir=.
OutputBaseFilename=LocalFileTools-Setup
SetupIconFile=LocalFileTools.ico
UninstallDisplayIcon={app}\LocalFileTools.exe
ChangesAssociations=yes
WizardStyle=modern

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: checkedonce
Name: explorer; Description: "Add LocalFileTools to File Explorer's right-click menu"; Flags: checkedonce

[Files]
Source: "package\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Check: UseX64
Source: "package-x86\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Check: UseX86
Source: "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.13/dotnet-runtime-10.0.13-win-x64.zip"; DestName: "dotnet-runtime.zip"; DestDir: "{app}\runtime"; ExternalSize: 36970749; Hash: "8db74a746baa450925d01325a2108ccf6ac39af4018047475292ae155c013d89"; Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion; Check: NeedPrivateX64Runtime
Source: "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.13/windowsdesktop-runtime-10.0.13-win-x64.zip"; DestName: "windowsdesktop-runtime.zip"; DestDir: "{app}\runtime"; ExternalSize: 39169839; Hash: "5b68b6739ff71ac862c2e90a50851483da041941ee76cf71b5dbb5d081c15870"; Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion; Check: NeedPrivateX64Runtime
Source: "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.13/dotnet-runtime-10.0.13-win-x86.zip"; DestName: "dotnet-runtime.zip"; DestDir: "{app}\runtime"; ExternalSize: 33968518; Hash: "f908a3b93ee1029e1518efbf89472eddd0625d5275baa97ad0b6c089807d6ee1"; Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion; Check: NeedPrivateX86Runtime
Source: "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.13/windowsdesktop-runtime-10.0.13-win-x86.zip"; DestName: "windowsdesktop-runtime.zip"; DestDir: "{app}\runtime"; ExternalSize: 36706741; Hash: "0628114565e0be5043585da7573624cc04f3e26aebcdc123423ab8bcf47aaabf"; Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion; Check: NeedPrivateX86Runtime

[Icons]
Name: "{userprograms}\LocalFileTools"; Filename: "{app}\LocalFileTools.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\LocalFileTools"; Filename: "{app}\LocalFileTools.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{usersendto}\LocalFileTools"; Filename: "{app}\LocalFileTools.exe"; Parameters: "--files"; WorkingDir: "{app}"; Tasks: explorer

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\LocalFileToolsV6"; ValueType: string; ValueName: ""; ValueData: "LocalFileTools"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\LocalFileToolsV6"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\LocalFileTools.exe"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\LocalFileToolsV6"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\LocalFileToolsV6\command"; ValueType: string; ValueName: ""; ValueData: """{app}\LocalFileTools.exe"" --files ""%1"""; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\LocalFileToolsV6"; ValueType: string; ValueName: ""; ValueData: "LocalFileTools"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\LocalFileToolsV6"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\LocalFileTools.exe"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\LocalFileToolsV6\command"; ValueType: string; ValueName: ""; ValueData: """{app}\LocalFileTools.exe"" --files ""%1"""; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\LocalFileToolsV6"; ValueType: string; ValueName: ""; ValueData: "LocalFileTools"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\LocalFileToolsV6"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\LocalFileTools.exe"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\LocalFileToolsV6\command"; ValueType: string; ValueName: ""; ValueData: """{app}\LocalFileTools.exe"" --files ""%1"""; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\LocalFileToolsV6"; ValueType: string; ValueName: ""; ValueData: "LocalFileTools"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\LocalFileToolsV6"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\LocalFileTools.exe"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\LocalFileToolsV6\command"; ValueType: string; ValueName: ""; ValueData: """{app}\LocalFileTools.exe"" --files ""%1"""; Tasks: explorer

[Run]
Filename: "{app}\LocalFileTools.exe"; Description: "Launch LocalFileTools"; Flags: nowait postinstall skipifsilent

[Code]
function UseX64: Boolean;
begin
  Result := IsX64Compatible;
end;

function UseX86: Boolean;
begin
  Result := not IsX64Compatible;
end;

function HasFramework(const DotnetRoot, FrameworkName, RequiredFile: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(DotnetRoot) + 'shared\' + FrameworkName + '\10.0.*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
           FileExists(AddBackslash(DotnetRoot) + 'shared\' + FrameworkName + '\' + FindRec.Name + '\' + RequiredFile) then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasGlobalDesktopRuntime(const ForX64: Boolean): Boolean;
var
  DotnetRoot: String;
begin
  Result := False;
  if ForX64 then
  begin
    if not RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', DotnetRoot) then
      DotnetRoot := ExpandConstant('{commonpf64}\dotnet');
  end
  else
  begin
    if not RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x86', 'InstallLocation', DotnetRoot) then
      DotnetRoot := ExpandConstant('{commonpf32}\dotnet');
  end;
  Result := FileExists(AddBackslash(DotnetRoot) + 'dotnet.exe') and
    HasFramework(DotnetRoot, 'Microsoft.NETCore.App', 'coreclr.dll') and
    HasFramework(DotnetRoot, 'Microsoft.WindowsDesktop.App', 'PresentationFramework.dll');
end;

function NeedPrivateX64Runtime: Boolean;
begin
  Result := UseX64 and not HasGlobalDesktopRuntime(True);
end;

function NeedPrivateX86Runtime: Boolean;
begin
  Result := UseX86 and not HasGlobalDesktopRuntime(False);
end;
