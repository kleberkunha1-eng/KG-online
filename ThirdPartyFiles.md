# Third-Party Files

Lista de DLLs/plugins de terceiros usados no build do jogo. Estes arquivos **nao** devem ser
reassinados pelo nosso pipeline - mantem a assinatura/propriedade original do fornecedor (quando
houver) e sao verificados apenas quanto a integridade (hash), nunca modificados.

## Runtime do proprio Unity (gerado automaticamente pelo Build Player)

- `UnityPlayer.dll`, `GameProjectKG_Data/*` - gerados pelo Unity Editor durante o build, assinados
  pela Microsoft/Unity Technologies quando aplicavel. Nao sao assinados pelo nosso pipeline.

## Networking (Mirror)

- `Mirror/` (codigo gerenciado, compilado junto com o projeto - nao e um binario de terceiros
  separado).
- `Mirror/Plugins/Mono.Cecil/*.dll` (Mono.CecilX, Mono.CecilX.Pdb, Mono.CecilX.Mdb,
  Mono.CecilX.Rocks) - usado apenas em tempo de **build/weaving no Editor**, nao e distribuido
  com o jogo final.
- `Mirror/Transports/Encryption/Plugins/BouncyCastle/Mirror.BouncyCastle.Cryptography.dll` -
  biblioteca de criptografia (Bouncy Castle), usada pelo transporte de rede com criptografia.

## Hosting (Edgegap)

- `Assets/Mirror/Hosting/Edgegap/` - plugin oficial do Edgegap para hospedagem de servidores
  dedicados (codigo gerenciado + ferramentas de build do Editor).

## Pacotes NuGet (via NuGetForUnity)

Dependencias gerenciadas (.NET) usadas por funcionalidades de autenticacao/serializacao, restaudas
via NuGetForUnity em `Packages/`:

- `Microsoft.IdentityModel.*` (Abstractions, JsonWebTokens, Logging, Tokens) e
  `System.IdentityModel.Tokens.Jwt` - validacao de tokens JWT.
- `System.Text.Json` e `Microsoft.Bcl.*` / `Microsoft.Extensions.*` - serializacao JSON e
  utilitarios base do .NET.
- `System.Security.Cryptography.Cng` - criptografia nativa do Windows.

Todos sao pacotes NuGet oficiais da Microsoft/mantenedores conhecidos, nao modificados pelo
projeto.

## Mobile Dependency Resolver (Google, apenas Editor)

- `MobileDependencyResolver/Editor/*.dll` (Google.IOSResolver, Google.JarResolver,
  Google.PackageManagerResolver, Google.VersionHandler*) - ferramentas de build do Editor para
  plataformas mobile, nao usadas no build Windows nem distribuidas com o jogo.

## Politica

- Nenhuma DLL de terceiros listada acima e reassinada pelo nosso pipeline de release.
- Antes de assinar qualquer arquivo novo, o pipeline verifica se ele foi **gerado pelo nosso
  proprio build** (jogo, launcher, instalador) e nao e um binario de terceiros ja assinado pelo
  fornecedor.
- Se um novo plugin/DLL nativo for adicionado ao projeto, atualize esta lista.
