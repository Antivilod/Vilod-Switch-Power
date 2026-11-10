#ifndef RepoRoot
  #error RepoRoot must be supplied by the build script
#endif
#define AppVersion "0.8.3"
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
[CustomMessages]
english.ExistingService=The QuietPC service is already registered. Turn off VSP control, let it restore your settings, then remove the previous version in Windows Settings > Apps > Installed apps. If it is not listed, use the previous version's own removal instructions.
russian.ExistingService=Служба QuietPC уже зарегистрирована. В старой VSP выключите управление, дождитесь восстановления настроек и удалите программу через Параметры Windows > Приложения > Установленные приложения. Если её там нет, следуйте инструкции по удалению старой версии.
english.OccupiedPath=The VSP installation path is already occupied (%1). Check for the previous VSP installation and its uninstaller before retrying.
russian.OccupiedPath=Путь установки VSP уже занят (%1). Проверьте, осталась ли предыдущая установка, и используйте её штатное удаление перед повторным запуском.
english.PreflightError=The installer could not check for an existing VSP installation. Close the installer, then rerun with /LOG to collect diagnostics.
russian.PreflightError=Установщик не смог проверить наличие предыдущей установки VSP. Закройте установщик и повторите запуск с параметром /LOG для диагностики.
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
  if not Exec(ExpandConstant('{tmp}\VSP.Setup.exe'), '--preflight', '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    Log(Format('VSP preflight could not start, error %d', [Code]));
    Result := CustomMessage('PreflightError');
    Exit;
  end;
  Log(Format('VSP preflight returned %d', [Code]));
  case Code of
    0: Result := '';
    20: Result := CustomMessage('ExistingService');
    21: Result := FmtMessage(CustomMessage('OccupiedPath'), [ExpandConstant('{app}')]);
  else
    Result := CustomMessage('PreflightError');
  end;
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
