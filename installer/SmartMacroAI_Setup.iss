; SmartMacroAI v1.5.7 — Inno Setup 6
; Created by Phạm Duy – Giải pháp tự động hóa thông minh.
;
; Build:   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish\SmartMacroAI
; Compile: ISCC.exe installer\SmartMacroAI_Setup.iss

#ifndef MyAppVersion
#define MyAppVersion "1.5.7"
#endif

#define MyAppName "SmartMacroAI"
#define MyAppPublisher "Phạm Duy"
#define MyAppURL "https://github.com/TroniePh/SmartMacroAI"
#define MyAppExeName "SmartMacroAI.exe"

[Setup]
AppId={{E4B8F9A2-7C31-4D6E-9B0A-1F2E3D4C5B6A}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE.txt
OutputDir=..\release
OutputBaseFilename=SmartMacroAI-v{#MyAppVersion}-win-x64-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
SetupIconFile=..\Assets\logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start with Windows"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
; SingleFile publish — chỉ cần 1 file .exe (driver đã embed bên trong)
Source: "..\publish\SmartMacroAI\SmartMacroAI.exe"; DestDir: "{app}"; Flags: ignoreversion
; PDB for crash diagnostics (optional)
Source: "..\publish\SmartMacroAI\SmartMacroAI.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Playwright browsers (optional, nếu có)
Source: "..\publish\SmartMacroAI\.playwright\*"; DestDir: "{app}\.playwright"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Components: playwright

[Components]
Name: "main"; Description: "SmartMacroAI Core (required)"; Types: full compact custom; Flags: fixed
Name: "playwright"; Description: "Web Automation Support (Playwright browsers)"; Types: full

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation (no web automation)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmartMacroAI"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{app}\.playwright"
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\macros"
Type: files; Name: "{app}\interception.dll"
