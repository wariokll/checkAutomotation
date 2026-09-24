#define MyAppName "UI Automation Inspector"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Wariokl"
#define MyAppExeName "UIAutomationInspectorWpf.exe"

[Setup]
AppId={{B4C4B1C2-0B64-4E65-9B71-8D9B6E5C1A31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\UIAutomationInspectorWpf
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=UIAutomationInspectorWpf-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные значки:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\UIAutomationInspectorWpf"
