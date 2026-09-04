; Inno Setup script for ClearPower (Windows x64). Built by windows/build.ps1, which passes
; /DAppVersion=<version> /DStageDir=<folder with ClearPower.exe> /DOutDir=<dist>.
; No service, no driver, no elevation: everything runs in the user's session, so the
; default is a per-user install (the admin option installs for all users).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef StageDir
  #define StageDir "stage"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

[Setup]
AppId={{9E5B7A9C-6C2E-4E0D-9F0B-3C1A2B4D5E6F}
AppName=ClearPower
AppVersion={#AppVersion}
AppVerName=ClearPower {#AppVersion}
AppPublisher=ClearPower contributors
AppPublisherURL=https://github.com/Clearailhc/ClearPower
AppSupportURL=https://github.com/Clearailhc/ClearPower/issues
AppUpdatesURL=https://github.com/Clearailhc/ClearPower/releases
DefaultDirName={autopf}\ClearPower
DefaultGroupName=ClearPower
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\ClearPower.exe
OutputDir={#OutDir}
OutputBaseFilename=ClearPower-Setup-{#AppVersion}-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile={#StageDir}\LICENSE
SetupIconFile=..\Sources\ClearPower\Resources\ClearPower.ico
MinVersion=10.0
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
#ifexist "compiler:Languages\ChineseSimplified.isl"
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#endif

[Tasks]
Name: "autostart"; Description: "Start ClearPower when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#StageDir}\ClearPower.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ClearPower"; Filename: "{app}\ClearPower.exe"
Name: "{autodesktop}\ClearPower"; Filename: "{app}\ClearPower.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClearPower"; ValueData: """{app}\ClearPower.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\ClearPower.exe"; Description: "Launch ClearPower"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\ClearPower.exe"; Parameters: "--quit"; RunOnceId: "quit"; Flags: runhidden

[Code]
// Ask a running instance to exit before files are replaced (upgrade) or removed.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\ClearPower.exe')) then
  begin
    Exec(ExpandConstant('{app}\ClearPower.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(800);
  end;
end;
