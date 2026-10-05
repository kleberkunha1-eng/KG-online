; GameInstaller.iss - Instalador opcional (Inno Setup) para o GAME PROJECT - K/G.
;
; IMPORTANTE SOBRE ARQUITETURA DESTE PROJETO:
; A distribuicao principal do jogo e feita pelo Launcher autoatualizavel (Launcher/), que ja
; implementa download incremental via HTTPS + verificacao de hash SHA-256 + reparo automatico
; (ver Launcher/tools/publish.js e Launcher/patcher.js). Isso cumpre, na pratica, o mesmo papel
; de seguranca de um instalador tradicional (integridade verificada, HTTPS, sem bypass de
; seguranca do Windows).
;
; Este script existe apenas como PREPARACAO OPCIONAL caso no futuro voce queira oferecer um
; instalador classico (Setup.exe) em vez de/alem do Launcher portatil. Ele NAO e executado
; automaticamente por nenhum pipeline - precisa ser compilado manualmente com o Inno Setup
; (https://jrsoftware.org/isinfo.php), que nao esta instalado neste projeto.
;
; Para compilar: abra este arquivo no Inno Setup Compiler (ISCC.exe) ou rode:
;   ISCC.exe Installer\GameInstaller.iss
; O Setup.exe gerado deve depois ser assinado com Tools\Signing\Sign-WindowsBuild.ps1 antes
; de ser distribuido.

#define MyAppName "GAME PROJECT - K/G Launcher"
#define MyAppPublisher "KG Studios"
#define MyAppURL "https://gamekg.pages.dev"
#define MyAppExeName "GameProjectKG-Launcher.exe"
; A versao real e injetada pelo pipeline de release via /DMyAppVersion=X.Y.Z na linha de
; comando do ISCC (nao hardcoded aqui).
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MySourceDir
  #define MySourceDir "..\Launcher\dist"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\Release"
#endif

[Setup]
AppId={{DEDEB5B3-8E71-499D-98F3-E15D4B10BAA1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\GameProjectKG
DisableProgramGroupPage=yes
OutputDir={#MyOutputDir}
OutputBaseFilename=GameProjectKG-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Nao requer privilegios de administrador para instalar (mesmo comportamento do Launcher hoje).
PrivilegesRequired=lowest

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; Este instalador deliberadamente NAO:
;  - desativa o Windows Defender ou SmartScreen;
;  - adiciona excecoes no antivirus;
;  - remove o Mark-of-the-Web de nenhum arquivo;
;  - requer privilegios administrativos alem do necessario para copiar os arquivos.
