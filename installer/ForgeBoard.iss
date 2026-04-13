#define MyAppName "ForgeBoard"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ForgeBoard"
#define MyAppURL "https://github.com/your-org/ForgeBoard"
#define MyAppExeName "ForgeBoard.Api.exe"

[Setup]
AppId={{B8F4D2A1-3C5E-4A7B-9D1F-2E6C8A0B4F3D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts
OutputBaseFilename=ForgeBoard-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupIconFile=..\src\ForgeBoard\Assets\favicon.ico
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ForgeBoard Web UI"; Filename: "http://localhost:{code:GetPort}"
Name: "{group}\ForgeBoard Configuration"; Filename: "{app}\appsettings.json"
Name: "{group}\Uninstall ForgeBoard"; Filename: "{uninstallexe}"

[Run]
Filename: "sc.exe"; Parameters: "create ForgeBoard binPath=""{app}\{#MyAppExeName}"" start=auto DisplayName=""ForgeBoard Build Server"""; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "description ForgeBoard ""Manages HashiCorp Packer VM image builds, feeds, and artifacts."""; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "start ForgeBoard"; Flags: runhidden waituntilterminated
Filename: "http://localhost:{code:GetPort}"; Description: "Open ForgeBoard in browser"; Flags: postinstall shellexec nowait skipifsilent unchecked

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop ForgeBoard"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete ForgeBoard"; Flags: runhidden waituntilterminated

[Code]
var
  PortPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  PortPage := CreateInputQueryPage(wpSelectDir,
    'Server Configuration', 'Configure the ForgeBoard server port.',
    'Specify the port number for the ForgeBoard web server. Default is 5050. Storage paths can be changed later in the ForgeBoard settings UI.');
  PortPage.Add('Port:', False);
  PortPage.Values[0] := '5050';
end;

function GetPort(Param: String): String;
begin
  Result := PortPage.Values[0];
  if Result = '' then
    Result := '5050';
end;

procedure WriteAppSettings;
var
  Port: String;
  Json: String;
begin
  Port := GetPort('');

  Json := '{' + #13#10;
  Json := Json + '  "ForgeBoard": {' + #13#10;
  Json := Json + '    "Port": ' + Port + ',' + #13#10;
  Json := Json + '    "DataDirectory": "",' + #13#10;
  Json := Json + '    "TempDirectory": ""' + #13#10;
  Json := Json + '  }' + #13#10;
  Json := Json + '}';

  SaveStringToFile(ExpandConstant('{app}\appsettings.json'), Json, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteAppSettings;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec('sc.exe', 'stop ForgeBoard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2000);
  Exec('sc.exe', 'delete ForgeBoard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;
