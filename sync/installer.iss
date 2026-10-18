; BTO Sync's installer (Inno Setup 6), built by build-zip.sh from the same files as the zip.
; Per-user install, no admin: the app updates itself by writing into its own folder (Updater.Apply).
; Signing: build-zip.sh passes /DSIGN and /Sbto=<command> when BTO_SIGN is set, and Inno signs setup and uninstaller.
#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Src
  #define Src "bin\Release\net48"
#endif

[Setup]
AppId={{6B1E3C9A-4F2D-4E8B-9C7A-B70517C0A2E1}
AppName=BTO Sync
AppVersion={#Version}
AppVerName=BTO Sync {#Version}
AppPublisher=BetterTBOIOnline
AppPublisherURL=https://github.com/MICHELOSCO/BetterTBOIOnline
DefaultDirName={autopf}\BTO Sync
DefaultGroupName=BTO Sync
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..
OutputBaseFilename=BTO-Sync-Setup-{#Version}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\BTO-Sync.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
#ifdef SIGN
SignTool=bto
SignedUninstaller=yes
#endif

[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#Src}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\BTO Sync"; Filename: "{app}\BTO-Sync.exe"
Name: "{autodesktop}\BTO Sync"; Filename: "{app}\BTO-Sync.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BTO-Sync.exe"; Description: "{cm:LaunchProgram,BTO Sync}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; old copies the self-updater renamed aside while running (Updater.Apply)
Type: files; Name: "{app}\*.old"
