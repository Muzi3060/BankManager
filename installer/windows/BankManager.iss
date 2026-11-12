; Instalador do BankManager para Windows (Inno Setup).
; Compilado por scripts/installers.py, que informa a versão e as pastas pelas opções /D da linha de comando.
; Por padrão instala só para o usuário atual (sem administrador), em %LOCALAPPDATA%\Programs\BankManager;
; no assistente, quem preferir pode escolher instalar para todos os usuários (pede administrador).
; Os dados financeiros ficam em %LOCALAPPDATA%\BankManager, fora da pasta do programa: atualizar ou desinstalar NÃO os apaga.

#ifndef AppVersion
  #error Informe a versão com /DAppVersion=x.y.z
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #error Informe a pasta publicada (win-x64) com /DSourceDir=...
#endif
#ifndef IconFile
  #error Informe o ícone (.ico) com /DIconFile=...
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; O AppId identifica o programa entre versões: instalar uma versão nova sobre a antiga a atualiza no lugar. Não altere este valor.
AppId={{8A5E1B6C-3D4F-4E27-9C1A-5B7D2F0E9A31}
AppName=BankManager
AppVersion={#AppVersion}
AppVerName=BankManager {#AppVersion}
AppPublisher=Muzi3060
AppPublisherURL=https://github.com/Muzi3060/BankManager
AppSupportURL=https://github.com/Muzi3060/BankManager/issues
VersionInfoVersion={#NumericVersion}
VersionInfoDescription=Instalador do BankManager
DefaultDirName={autopf}\BankManager
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\BankManager.exe
SetupIconFile={#IconFile}
OutputDir={#OutputDir}
OutputBaseFilename=BankManager-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Se o programa estiver aberto durante uma atualização, o instalador pede para fechá-lo.
CloseApplications=yes
RestartApplications=no

[Languages]
; O português do Brasil acompanha o Inno Setup; se a instalação do compilador não o trouxer, o instalador sai em inglês.
#if FileExists(AddBackslash(CompilerPath) + "Languages\BrazilianPortuguese.isl")
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BankManager"; Filename: "{app}\BankManager.exe"; Comment: "Finanças pessoais e familiares"
Name: "{autodesktop}\BankManager"; Filename: "{app}\BankManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BankManager.exe"; Description: "{cm:LaunchProgram,BankManager}"; Flags: nowait postinstall skipifsilent
