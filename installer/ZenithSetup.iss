; =====================================================================
; Inno Setup Script for Zenith Utility
; Modern, high-performance productivity tool for Windows 10/11
; =====================================================================

#define MyAppName "Zenith Utility"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Orion-style"
#define MyAppURL "https://github.com/Orion-style/ScreenText"
#define MyAppExeName "ZenithApp.exe"
#define MyAppMutex "ZenithUtility"

[Setup]
; Unique App ID (GUID) for version upgrades and uninstall tracking
AppId={{91B2E8D7-34E9-4E64-8846-DC8321B07412}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=..\Release
OutputBaseFilename=ZenithSetup-{#MyAppVersion}-x64
SetupIconFile=..\ZenithApp\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

; Architecture: 64-bit native Windows
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Dual-mode: lowest privileges (per-user AppData) or admin (Program Files)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Mutex and process handling for safe install/upgrade/uninstall
AppMutex={#MyAppMutex}
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}

; Version metadata embedded into installer executable
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion=1.0.0.0
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.CreateDesktopIcon=Create a &desktop shortcut
russian.CreateDesktopIcon=Создать значок на &Рабочем столе
english.AdditionalIcons=Additional shortcuts:
russian.AdditionalIcons=Дополнительные ярлыки:
english.StartupGroup=Startup settings:
russian.StartupGroup=Параметры запуска:
english.AutoStartTask=Launch {#MyAppName} automatically when Windows starts
russian.AutoStartTask=Запускать {#MyAppName} автоматически при входе в Windows
english.LaunchProgram=Launch {#MyAppName} now
russian.LaunchProgram=Запустить {#MyAppName} сейчас

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:StartupGroup}"; Flags: unchecked

[Files]
; Main application binaries from publish directory
Source: "..\build_output\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\ZenithApp\Resources\app.ico"; DestDir: "{app}\Resources"; Flags: ignoreversion
Source: "..\ZenithApp\Resources\app.png"; DestDir: "{app}\Resources"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\README.ru.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu and Desktop shortcuts
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Registry]
; Optional autorun in HKCU Run registry
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ZenithUtility"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; Option to launch the application immediately upon installation completion
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram}"; Flags: nowait postinstall skipifsilent

[Code]
// Ensures clean experience during install and uninstall
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
