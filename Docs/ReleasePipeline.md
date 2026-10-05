# Release Pipeline - GAME PROJECT K/G

Visao geral do pipeline de build + assinatura + empacotamento + release para Windows.

## Fluxo completo

```
Unity Project
  v
Unity Windows Build        (Tools > Build > Windows Release, ou Tools/Release/Build-Release.ps1)
  v
Validate Build              (BuildPipeline.BuildPlayer: 0 erros, resultado Succeeded)
  v
Sign EXE                    (Tools/Signing/Sign-WindowsBuild.ps1 - se SIGNING_MODE configurado)
  v
Verify Signature             (Tools/Signing/Verify-Signature.ps1 - aborta release se invalida)
  v
Generate Checksums + Manifest (Tools/Release/Generate-Manifest.ps1 -> checksums.sha256 + release-manifest.json)
  v
(Opcional) Build Installer    (Installer/GameInstaller.iss via Inno Setup)
  v
(Opcional) Sign + Verify Installer
  v
Release Ready                (Release/GameProjectKG-<versao>/)
```

## Estrutura de pastas

```
Assets/Editor/Build/
  BuildReleaseConfig.cs       - configuracao central (sem segredos)
  WindowsBuildPipeline.cs     - menu Tools/Build/* dentro do Unity Editor

Tools/
  Signing/
    Sign-WindowsBuild.ps1     - abstracao de assinatura (None/LocalCertificate/AzureArtifactSigning)
    Verify-Signature.ps1      - verifica assinatura, aborta release se invalida
  Release/
    Generate-Manifest.ps1     - gera checksums.sha256 + release-manifest.json
    Build-Release.ps1         - orquestra o pipeline completo (com -DryRun)

Installer/
  GameInstaller.iss           - instalador opcional (Inno Setup) - nao obrigatorio

Build/GameProjectKG/           - saida da build do Unity (ignorado pelo Git)
Release/GameProjectKG-<versao>/ - pacote final com checksums + manifest (ignorado pelo Git)
```

## Como usar (dentro do Unity Editor)

- **Tools > Build > Windows Development**: build rapida, assinatura opcional, para testar
  localmente.
- **Tools > Build > Windows Release**: build de producao - versiona (PlayerSettings.bundleVersion
  + commit curto do Git como Build ID), assina (se `SIGNING_MODE` estiver configurado), verifica
  a assinatura, gera `checksums.sha256` + `release-manifest.json`. Se a assinatura falhar na
  verificacao, o processo e interrompido com erro no console - nunca publica uma build
  "quase assinada".
- **Tools > Build > Verify Release**: so roda a verificacao de assinatura na build existente
  (util para conferir antes de publicar manualmente).

## Como usar (linha de comando / fora do Unity)

> **Nota sobre Execution Policy**: por padrao o Windows bloqueia a execucao de scripts `.ps1`
> baixados/criados localmente (`Restricted`). Use `-ExecutionPolicy Bypass` apenas para esta
> chamada (nao altera a politica global do sistema, nem desativa nenhuma protecao permanente):
>
> ```powershell
> powershell -ExecutionPolicy Bypass -File Tools\Release\Build-Release.ps1 -DryRun
> ```

```powershell
# Dry run: mostra o que seria feito, sem build/assinatura/alteracao de arquivos reais
powershell -ExecutionPolicy Bypass -File Tools\Release\Build-Release.ps1 -DryRun

# Release completo (requer Unity Editor instalado no caminho padrao do Unity Hub)
$env:SIGNING_MODE = 'LocalCertificate'
$env:SIGN_CERT_PATH = 'C:\Certificates\game.pfx'
$env:SIGN_CERT_PASSWORD = '********'
powershell -ExecutionPolicy Bypass -File Tools\Release\Build-Release.ps1 -Version 1.0.0

# Pular o build do Unity (usar uma build ja existente em Build/GameProjectKG)
powershell -ExecutionPolicy Bypass -File Tools\Release\Build-Release.ps1 -SkipUnityBuild -Version 1.0.0
```

## Versionamento

- Fonte de versao: `PlayerSettings.bundleVersion` (`ProjectSettings/ProjectSettings.asset`).
- Build ID: commit curto do Git (`git rev-parse --short HEAD`), quando disponivel - o Git nunca
  é obrigatorio para abrir ou buildar o projeto, apenas enriquece o relatorio quando presente.
- A mesma versao/build aparece em: `GameProjectKG.exe` (metadados), `release-manifest.json` e,
  se gerado, no instalador.

## Integracao com o Launcher existente

O Launcher (`Launcher/`) ja implementa, para os arquivos do JOGO, exatamente o fluxo descrito na
secao 18 do pedido original (manifest remoto -> comparar versao local -> comparar hashes SHA-256
-> baixar so o que mudou -> verificar hash -> substituir com seguranca) - ver
[Launcher/COMO-ENVIAR-UPDATES.md](../Launcher/COMO-ENVIAR-UPDATES.md). O pipeline de signing
descrito aqui assina os executaveis ANTES desse manifest ser gerado e publicado, entao o
jogador sempre recebe arquivos ja assinados (quando `SIGNING_MODE` estiver configurado).

Para o proprio Launcher, `Launcher/tools/build-launcher.js` agora usa a mesma abstracao de
assinatura (em vez de deixar o electron-builder assinar implicitamente com qualquer certificado
presente no certificate store local).

## Instalador (opcional)

A distribuicao principal deste projeto e o Launcher autoatualizavel (portatil, com download
incremental + verificacao de hash via HTTPS), o que ja cobre as garantias de seguranca que um
instalador tradicional ofereceria. `Installer/GameInstaller.iss` fica preparado caso voce queira,
no futuro, oferecer tambem um `Setup.exe` classico - requer instalar o
[Inno Setup](https://jrsoftware.org/isinfo.php) manualmente (nao incluso neste projeto) e nao e
executado automaticamente por nenhum pipeline.

## Dry Run

Toda execucao do `Build-Release.ps1` aceita `-DryRun`: mostra quais arquivos seriam assinados,
quais comandos de `signtool`/`AzureSignTool` seriam executados, e quais pastas/arquivos seriam
gerados - sem assinar, buildar ou modificar nada de verdade. Use isso para validar o pipeline
antes de configurar um certificado real.

## Relatorio final

Ao concluir, o pipeline imprime um resumo (formato inspirado na secao 36 do pedido original):

```
====================================
WINDOWS RELEASE

Game:            GAME PROJECT - K/G
Version:         1.0.0
Build:           a75cd3f
Unity Build:     OK
Game.exe:        SIGNED
Launcher.exe:    SIGNED
Installer:       SKIPPED (Inno Setup nao instalado)
Timestamp:       OK
SHA256:          GENERATED
Manifest:        GENERATED
Release dir:     Release/GameProjectKG-1.0.0
Release:         READY
====================================
```

Se `SIGNING_MODE` nao estiver configurado, o relatorio mostra claramente `NOT SIGNED
(SIGNING_MODE=None)` e um aviso `CODE SIGNING NOT CONFIGURED` - o pipeline nunca finge uma
assinatura nem cria um certificado autoassinado automaticamente para producao.
