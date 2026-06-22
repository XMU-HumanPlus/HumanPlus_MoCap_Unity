#define MyAppName "HumanPlusMoCapUnity"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HumanPlusMoCapUnity"
#define MyAppExeName "HumanPlusMoCapUnity.exe"
#define MyDistDir "..\Build\Windows\HumanPlusMoCapUnity"
#define MyLicenseFile "EULA-zh-CN.txt"

[Setup]
AppId={{4B63E8F2-0A6D-4B47-8B0A-1C328E6D5A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile={#MyLicenseFile}
OutputDir=..\Build\Installer
OutputBaseFilename=HumanPlusMoCapUnity-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "{#MyDistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
