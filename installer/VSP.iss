#ifndef RepoRoot
  #error RepoRoot must be supplied by the build script
#endif
#define AppVersion "0.8.1"
[Setup]
AppId={{B70F777F-8ACD-413D-945E-212A6390C227}
AppName=Vilod Switch Power
AppVersion={#AppVersion}
AppPublisher=Vilod
DefaultDirName={commonpf64}\Vilod\VSP
DisableDirPage=yes
DefaultGroupName=Vilod Switch Power
DisableProgramGroupPage=yes
OutputDir={#RepoRoot}\artifacts\release
OutputBaseFilename=VSP-{#AppVersion}-Setup-x64
SetupIconFile={#RepoRoot}\src\QuietPC.App\Assets\vsp.ico
UninstallDisplayIcon={app}\app\QuietPC.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
PrivilegesRequired=admin
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=Vilod Switch Power {#AppVersion}
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Files]
Source: "{#RepoRoot}\artifacts\source-build\setup\VSP.Setup.exe"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{#RepoRoot}\artifacts\source-build\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\artifacts\source-build\monitor\*"; DestDir: "{app}\monitor"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\artifacts\source-build\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\artifacts\source-build\setup\VSP.Setup.exe"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD-PARTY.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\NOTICE"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\licenses\*"; DestDir: "{app}\licenses\third-party"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\artifacts\source-build\runtime-licenses\*"; DestDir: "{app}\licenses\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{commonprograms}\Vilod Switch Power"; Filename: "{app}\app\QuietPC.exe"; WorkingDir: "{app}\app"
[Run]
Filename: "{app}\app\QuietPC.exe"; Description: "Open Vilod Switch Power"; Flags: nowait postinstall skipifsilent unchecked runasoriginaluser
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  ExtractTemporaryFile('VSP.Setup.exe');
  if (not Exec(ExpandConstant('{tmp}\VSP.Setup.exe'), '--preflight', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
    Result := 'VSP cannot be installed over an existing QuietPC/VSP service or installation directory. Use the previous version''s uninstaller first. No existing service will be replaced.';
end;
procedure CurStepChanged(Step: TSetupStep);
var Code: Integer;
begin
  if Step = ssPostInstall then
    if (not Exec(ExpandConstant('{app}\setup\VSP.Setup.exe'), '--install', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
      RaiseException('The power service could not be configured. Installation is incomplete; do not enable power control. Use the uninstaller and keep recovery data.');
end;
function InitializeUninstall(): Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{app}\setup\VSP.Setup.exe'), '--remove', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then
    MsgBox('Power restoration was not confirmed. Uninstall was stopped; application files and recovery data were preserved. Close VSP and resolve the recovery conflict before retrying.', mbError, MB_OK);
end;
