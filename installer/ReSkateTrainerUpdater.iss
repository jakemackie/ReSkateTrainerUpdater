; Inno Setup script for ReSkate Trainer Updater. Built by scripts/build-release.ps1, which passes the version and the exe:
;   ISCC /DAppVersion=1.3.0 /DSourceExe=C:\...\ReSkateTrainerUpdater.exe installer\ReSkateTrainerUpdater.iss
; See docs/ARCHITECTURE.md ("The installer") before changing the rules below.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\release\publish\ReSkateTrainerUpdater.exe"
#endif

#define AppName "ReSkate Trainer Updater"
#define AppExe "ReSkateTrainerUpdater.exe"
#define RepoUrl "https://github.com/jakemackie/ReSkateTrainerUpdater"

[Setup]
; The app's permanent identity. It is how a newer setup finds and replaces an older install, and how the app finds its own
; Add/Remove Programs entry. It must never change, and must match AppInfo.InstallerAppId in the app.
AppId={{A0B385CA-E8B7-4191-93AA-E09E62F2C52B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=os_uk
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} setup
VersionInfoCompany=os_uk

; Installed for the current user only, into their own app folder, and never as administrator. This is what lets the app
; replace its own exe when it updates itself (a folder like C:\Program Files isn't writable without admin rights).
; Don't add PrivilegesRequiredOverridesAllowed: an all-users install would break self-update.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir=..\release
OutputBaseFilename=ReSkateTrainerUpdater-Setup-{#AppVersion}
SetupIconFile=..\src\ReSkateTrainerUpdater\Assets\ReSkateTrainerUpdater.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
; The exe is already compressed, so more compression only makes setup slower to build.
Compression=lzma2/fast
SolidCompression=no

; If the app is open, close it for the install, and again for an uninstall.
CloseApplications=yes
CloseApplicationsFilter={#AppExe}
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Installs the newest ReSkate Trainer into your ReSkate folder"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The app swaps itself for a newer version by renaming, which leaves these beside the exe for a moment.
Type: files; Name: "{app}\{#AppExe}.old"
Type: files; Name: "{app}\{#AppExe}.new"

[Code]
// After uninstalling, offer to forget the saved settings (just the remembered ReSkate folder and the backup switch).
// A silent uninstall keeps them, so reinstalling later picks up where the user left off.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Also remove your saved settings (the ReSkate folder it remembered)?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\ReSkateTrainerUpdater'), True, True, True);
end;
