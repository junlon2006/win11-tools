#ifndef PublishDir
  #define PublishDir "..\artifacts\win-x64"
#endif

#ifndef SetupOutputDir
  #define SetupOutputDir "..\artifacts\installer"
#endif

#define AppName "CPU 核心电压"
#define AppExeName "CpuCoreVoltageTray.exe"
#define AppVersion "1.0.0"
#define AppMutexName "Local\CpuCoreVoltageTray-5D5A5ED5-9ECB-4C38-9D59-171355D3D54A"

[Setup]
AppId={{5D5A5ED5-9ECB-4C38-9D59-171355D3D54A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CpuCoreVoltageTray
DefaultDirName={autopf}\CpuCoreVoltageTray
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
AppMutex={#AppMutexName}
SetupIconFile=..\src\CpuCoreVoltageTray\Resources\app.ico
UninstallDisplayIcon={app}\app.ico
OutputDir={#SetupOutputDir}
OutputBaseFilename=CpuCoreVoltageTray-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "autostart"; Description: "登录 Windows 时自动启动"; GroupDescription: "启动选项："
Name: "desktopicon"; Description: "为所有用户创建桌面快捷方式"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\SETUP.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
Type: files; Name: "{commondesktop}\{#AppName}.lnk"
Type: files; Name: "{app}\autostart-install-error.log"
Type: files; Name: "{app}\autostart-remove-error.log"

[Code]
function RunAutoStartCommand(const Parameters: String; var ResultCode: Integer): Boolean;
begin
  ResultCode := -1;
  Result := Exec(
    ExpandConstant('{app}\{#AppExeName}'),
    Parameters,
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
  Result := Result and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Parameters: String;
  ResultCode: Integer;
  CleanupResultCode: Integer;
  ErrorLogPath: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  if WizardIsTaskSelected('autostart') then
    Parameters := '--install-autostart'
  else
    Parameters := '--remove-autostart';

  if Parameters = '--install-autostart' then
    ErrorLogPath := ExpandConstant('{app}\autostart-install-error.log')
  else
    ErrorLogPath := ExpandConstant('{app}\autostart-remove-error.log');

  if not RunAutoStartCommand(Parameters, ResultCode) then
  begin
    if Parameters = '--install-autostart' then
    begin
      if not RunAutoStartCommand('--remove-autostart', CleanupResultCode) then
        Log(Format('补偿删除登录自动启动任务失败，程序退出代码：%d', [CleanupResultCode]));
    end;

    RaiseException(Format('无法配置登录自动启动，程序退出代码：%d。详情：%s', [ResultCode, ErrorLogPath]));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  if not RunAutoStartCommand('--remove-autostart', ResultCode) then
    RaiseException(Format('无法删除登录自动启动任务，程序退出代码：%d。详情：%s', [ResultCode, ExpandConstant('{app}\autostart-remove-error.log')]));
end;
