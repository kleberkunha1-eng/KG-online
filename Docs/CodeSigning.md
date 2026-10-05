# Code Signing - GAME PROJECT K/G

Este documento explica como a assinatura digital (Authenticode) dos executaveis do jogo
(`GameProjectKG.exe`, `GameProjectKG-Launcher.exe` e, se gerado, o instalador `Setup.exe`)
funciona neste projeto, e como configurar um certificado real quando voce tiver um.

## Como funciona

Todo o pipeline usa uma unica abstracao central:
[Tools/Signing/Sign-WindowsBuild.ps1](../Tools/Signing/Sign-WindowsBuild.ps1), controlada pela
variavel de ambiente `SIGNING_MODE`:

| SIGNING_MODE | O que faz | Quando usar |
|---|---|---|
| `None` (padrao) | Nao assina nada | Desenvolvimento local |
| `LocalCertificate` | Assina com um certificado `.pfx` local via `signtool.exe` | Quando voce comprar um certificado Authenticode tradicional (OV/EV) |
| `AzureArtifactSigning` | Assina via **Azure Trusted Signing** (assinatura em nuvem, sem precisar guardar um `.pfx`) | Alternativa gerenciada pela Microsoft, custo mensal mais previsivel |

**Nunca** commit um certificado (`.pfx`/`.p12`/`.key`/`.pem`), senha ou credencial no Git - o
`.gitignore` ja bloqueia essas extensoes e as pastas `Secrets/`/`Certificates/`.

## Configurar o modo LocalCertificate

1. Compre/obtenha um certificado Authenticode (ver seção "Sobre custos" abaixo).
2. Exporte-o como `.pfx` protegido por senha e guarde-o **fora do repositorio** (ex.:
   `C:\Certificates\game.pfx`, nunca dentro da pasta do projeto).
3. Defina as variaveis de ambiente (nunca em arquivo versionado):
   ```powershell
   $env:SIGNING_MODE = 'LocalCertificate'
   $env:SIGN_CERT_PATH = 'C:\Certificates\game.pfx'
   $env:SIGN_CERT_PASSWORD = '********'
   $env:SIGN_TIMESTAMP_URL = 'http://timestamp.digicert.com'
   ```
4. Rode o release normalmente (`Tools/Release/Build-Release.ps1` ou o menu
   `Tools > Build > Windows Release` no Unity). O `signtool.exe` do Windows SDK é localizado
   automaticamente (nao precisa estar no PATH).

## Configurar o modo AzureArtifactSigning (Azure Trusted Signing)

1. Crie uma conta Trusted Signing no Azure Portal e um "Certificate Profile" (identidade
   verificada, OV).
2. Instale a ferramenta de assinatura: `dotnet tool install --global AzureSignTool`.
3. Autentique-se via Azure CLI (`az login`) ou configure uma Managed Identity/Service Principal
   (variaveis `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_CLIENT_SECRET` padrao do Azure SDK -
   nunca grave essas credenciais no projeto).
4. Defina:
   ```powershell
   $env:SIGNING_MODE = 'AzureArtifactSigning'
   $env:AZURE_SIGNING_ACCOUNT = 'minha-conta-trusted-signing'
   $env:AZURE_SIGNING_PROFILE = 'meu-certificate-profile'
   $env:AZURE_SIGNING_ENDPOINT = 'https://<regiao>.codesigning.azure.net'
   ```

## Verificar uma assinatura

```powershell
powershell -ExecutionPolicy Bypass -File Tools\Signing\Verify-Signature.ps1 -Files "Build\GameProjectKG\GameProjectKG.exe"
```

Mostra `Status`, `SignerCertificate`, `Subject`, `Issuer` e o carimbo de tempo (timestamp). Se a
assinatura for invalida ou ausente, o script retorna codigo de saida diferente de zero - use isso
como portao de qualidade antes de publicar (o `Build-Release.ps1` ja faz isso automaticamente e
aborta o release se a verificacao falhar).

## Dry run (testar sem assinar nada de verdade)

```powershell
powershell -ExecutionPolicy Bypass -File Tools\Release\Build-Release.ps1 -DryRun
```

Mostra exatamente quais arquivos seriam assinados e quais comandos seriam executados, sem
modificar nenhum arquivo nem exigir um certificado configurado.

## Sobre custos de certificado (2026)

Um certificado Authenticode legitimo (OV - Organization Validation) custa, na faixa mais barata
encontrada, aproximadamente US$ 64-68/ano (ex.: SSL.com com desconto) - nao existem opcoes
reputadas abaixo de ~US$ 50/ano. **Nem certificados pagos removem o aviso do SmartScreen
imediatamente** - desde 2024 a Microsoft exige que a reputacao do aplicativo tambem acumule com
o tempo/uso, independente do certificado ter ou nao uma cadeia confiavel. A diferenca pratica de
um certificado pago e que sua cadeia ja e confiavel desde o primeiro dia (permite que a
reputacao comece a acumular), enquanto um certificado autoassinado nunca constroi reputacao
(cadeia sempre rejeitada pelo Windows). Ver [Docs/WindowsSecurity.md](./WindowsSecurity.md) para
mais detalhes sobre SmartScreen/Defender.

**Um certificado autoassinado (`SIGNING_MODE=LocalCertificate` com um `.pfx` gerado localmente)
nunca deve ser apresentado como solucao para distribuicao publica** - serve apenas para testes
internos/desenvolvimento, pois sua cadeia de confianca nunca sera reconhecida pelo Windows de um
jogador.

## Arquivos assinados pelo pipeline

- `GameProjectKG.exe` (jogo, gerado pelo Unity)
- `GameProjectKG-Launcher.exe` (launcher, Electron)
- `GameProjectKG-Setup-<versao>.exe` (instalador opcional, se gerado via Inno Setup)

DLLs de terceiros (Unity Player runtime, Mirror, Edgegap, etc.) **nao** sao reassinadas - ver
[ThirdPartyFiles.md](../ThirdPartyFiles.md).
