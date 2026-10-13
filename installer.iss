; Instalador do MTG Estoque (Inno Setup 6).
; Gerar: dotnet publish (autocontido) em .\publish e depois:  ISCC installer.iss
; Instala só para o usuário atual (sem pedir admin) e pergunta onde guardar a coleção e o cache.

#define AppName "MTG Estoque"
#define AppVersion "1.0"
#define AppExe "MtgEstoque.exe"

[Setup]
AppId={{6E3C2B7A-5D1F-4C9E-9B2A-MTGESTOQUE01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Gabriel
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=MTG-Estoque-Setup-{#AppVersion}
SetupIconFile=Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"

[Files]
Source: "publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; a coleção e o cache NÃO são apagados ao desinstalar — só o arquivo que diz onde eles ficam
Type: files; Name: "{app}\datadir.txt"

[Code]
var
  DataPage: TInputDirWizardPage;

procedure InitializeWizard;
begin
  DataPage := CreateInputDirPage(wpSelectDir,
    'Onde guardar seus dados',
    'Escolha a pasta da sua coleção e do cache de imagens.',
    'O MTG Estoque guarda aqui o arquivo da coleção (decks, binders, movimentações) e as imagens ' +
    'baixadas do Scryfall. Pode ser uma pasta sincronizada (OneDrive, Google Drive) se quiser ' +
    'backup automático. Desinstalar o app não apaga essa pasta.',
    False, '');
  DataPage.Add('Pasta dos dados:');
  DataPage.Values[0] := ExpandConstant('{userappdata}\MtgEstoque');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ForceDirectories(DataPage.Values[0]);
    SaveStringToFile(ExpandConstant('{app}\datadir.txt'), DataPage.Values[0], False);
  end;
end;
