; Instalador do Levhrium.
;
; Instalacao por usuario, sem admin: e isso que permite o botao "Atualizar"
; dentro do navegador trocar os arquivos sem pedir elevacao toda vez.
;
; Compilar com: build.ps1 (publica o app e chama o ISCC)

#define AppName "Levhrium"
#define AppPublisher "willoliveira1"
#define AppUrl "https://github.com/willoliveira1/levhrium"
#define AppExe "Levhrium.exe"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{8F2C5A31-4D7E-4C19-9B6A-7E3D1F8A2C45}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Por usuario: nao pede admin, e o atualizador consegue escrever aqui.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

OutputDir=..\dist
OutputBaseFilename=Levhrium-Setup-{#AppVersion}
SetupIconFile=..\build\levhrium.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; O Levhrium so roda em Windows 10 1809 (build 17763) ou mais novo, x64:
; e o minimo do runtime WebView2.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; Fecha o navegador antes de substituir os arquivos. Sem isto, atualizar por
; cima de uma instalacao aberta falharia com arquivos em uso.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar o {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

{ O Levhrium usa o runtime WebView2 do sistema. Sem ele o navegador nao abre,
  entao o instalador cuida disso em vez de deixar o erro para o usuario. }
function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;

  { Instalacao por maquina, registrada no ramo 32 bits do registro. }
  if RegQueryStringValue(HKEY_LOCAL_MACHINE,
       'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
       'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then
      Result := True;

  if Result then
    Exit;

  { Instalacao por usuario. }
  if RegQueryStringValue(HKEY_CURRENT_USER,
       'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
       'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then
      Result := True;
end;

function OnDownloadProgress(const Url, Filename: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(
    'Dependencia do navegador',
    'Baixando o runtime WebView2 da Microsoft',
    @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if CurPageID <> wpReady then
    Exit;

  if IsWebView2Installed() then
    Exit;

  { Link oficial do bootstrapper Evergreen: ele baixa e instala a versao atual
    do runtime, e a Microsoft passa a mante-lo atualizado sozinha. }
  DownloadPage.Clear();
  DownloadPage.Add(
    'https://go.microsoft.com/fwlink/p/?LinkId=2124703',
    'MicrosoftEdgeWebview2Setup.exe',
    '');
  DownloadPage.Show();

  try
    try
      DownloadPage.Download();

      if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
                  '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox('Nao foi possivel instalar o runtime WebView2.'#13#10 +
               'O Levhrium sera instalado, mas so vai abrir depois que o runtime estiver presente.',
               mbInformation, MB_OK);
      end;
    except
      MsgBox('Falhou ao baixar o runtime WebView2:'#13#10 + GetExceptionMessage + #13#10#13#10 +
             'Verifique a conexao e tente de novo, ou instale o WebView2 pelo site da Microsoft.',
             mbError, MB_OK);
      Result := False;
    end;
  finally
    DownloadPage.Hide();
  end;
end;
