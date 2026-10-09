# Ambiente local restaurado

O projeto ativo esta em `C:\Tales of Pirates Unity`. A copia em `C:\KG Online`
continua sendo o backup e nao deve ser aberta como projeto Unity.

## Banco e API

- Banco restaurado: MariaDB **12.3.2**, servico **TOP-MariaDB12**,
  conta `NT AUTHORITY\LocalService`, inicio manual, **127.0.0.1:3307**.
- O MariaDB **13.0** ja instalado, na porta **3306**, foi preservado.
- Configuracao do banco restaurado: `C:\TOP-Restoration\mariadb-12.3\local.ini`.
- Dump antes das migrations da API:
  `C:\TOP-Restoration\mariadb-12.3\top_unity-before-api.sql`.
- A senha do banco restaurado e a do backup em `.env`, nao necessariamente
  a senha escolhida para o MariaDB 13.0 depois da reinstalacao.
- O `.env` original foi preservado. `.env.local` seleciona host/portas locais
  e desativa a criacao automatica da conta de teste, inclusive em `npm start`.
  Variaveis de ambiente explicitas continuam tendo prioridade. `start-local.ps1`
  tambem usa Node.js **24.16.0** sem substituir o Node.js instalado no Windows.

Para iniciar o banco apos reiniciar o Windows, execute em PowerShell elevado:

```powershell
Start-Service TOP-MariaDB12
```

Para iniciar a API sem tunel publico:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Tales of Pirates Unity\API\start-local.ps1"
```

A API local escuta somente em `http://127.0.0.1:3000`. O suporte a `HOST` em
`server.js` permite esse isolamento. Sem `.env.local` e sem `HOST`, o padrao do
codigo continua sendo o anterior, em todas as interfaces. Nao use `start-online.ps1`
para testes locais: ele pode iniciar um tunel publico.

As migrations de `server.js` foram revisadas e o dump foi produzido antes da
primeira inicializacao. Nao execute migrations SQLite/D1 no MariaDB.

## Separacao do publicado

- API publicada: `https://gamekg.pages.dev`.
- Cloudflare Pages: projeto `gamekg`; D1: banco `gamekg-db`.
- Configuracao Pages recuperada em `..\Cloudflare\wrangler.toml`.
- Snapshot D1 local em `C:\TOP-Restoration\private\cloudflare-state`, com
  integridade SQLite verificada. Nao usa o banco remoto durante o desenvolvimento.
- Execute `..\Cloudflare\start-local.cmd` para servir Pages/Functions com o D1
  local em `http://127.0.0.1:3001`. O JWT de `.dev.vars` foi gerado somente
  para esse ambiente; nao substitui o segredo publicado.
- `..\Cloudflare\wrangler-local.cmd` usa o Node local e o cofre Windows do
  Wrangler. Os logins antigos protegidos pelo Windows nao foram reutilizados.
- Nenhum deploy ou SQL de escrita remoto foi executado.
- O Editor conserva `Tools\editor-server-address.txt`, porta **7778**.
  O `api.json` na raiz do projeto seleciona **http://127.0.0.1:3000**, somente
  nesta copia local; esse arquivo e ignorado pelo Git. Contas/JWT agora sao do
  MariaDB local, nao do Cloudflare. A configuracao das builds publicadas nao mudou.
- O publicado conserva a porta **7777**. A build ativa nao foi substituida.
- Para um teste inteiramente local, passe `--api=http://127.0.0.1:3000` ao
  Editor/cliente e ao servidor de teste. Nao misture contas/JWT de MariaDB
  local com os do D1 publicado.

Playit foi instalado sem ativacao do agente; seu segredo foi preservado em
`C:\TOP-Restoration\private\playit-inactive-backup`. A tarefa `TOP-DedicatedServer`
foi recriada **desativada**, com usuario e caminho atuais. Nenhuma regra de
firewall foi aberta. Nao ative a tarefa/tunel antes de autorizacao para exposicao
e validacao de staging.

Para iniciar o servidor de jogo usado pelo Play no Unity, com banco e API local
ja iniciados:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Tales of Pirates Unity\Tools\Release\Start-EditorServer.ps1"
```

Esse script usa exclusivamente `Build\GameProjectKG.editorserver`, UDP **7778**
e `--api=http://127.0.0.1:3000`, sem playit, publicacao ou alteracoes de firewall.
A build e gerada com subtarget **Windows Dedicated Server**, sem inicializar
audio/renderizacao; o cliente no Unity mantem seu audio normal. A build antiga
`GameProjectKG.servertest` era uma build de cliente e reproduzia musica mesmo
com `-noaudio`, portanto nao e mais usada por esse script.
A porta KCP escuta nas interfaces locais (nao apenas loopback);
nenhum tunel ou encaminhamento foi ativado. Fechar o script interrompe o servidor.
Saia do Play e entre novamente para o Editor recarregar o `api.json`.
Use/crie uma conta local; contas criadas no Cloudflare nao foram importadas para
o MariaDB. O Trace confirmou login HTTP 200 seguido de desconexao antes de
iniciar esse servidor, porque nao havia processo na porta 7778.

Para gerar essa build separada, use **Tools > Build Local Dedicated Server
(Windows)** no Editor. O destino deve estar ausente; o gerador nao apaga uma
build local preexistente nem promove/substitui a build publicada.

A build Windows Dedicated Server local foi concluida em 08/10/2026:
**Succeeded | 0 errors | 00:07:31**, com hash do executavel publicado inalterado.
O servidor foi iniciado e o handshake KCP real em `127.0.0.1:7778` passou,
sem autenticacao nem escrita no banco durante essa verificacao. O log confirmou
`NullGfxDevice`, escuta UDP 7778 e carregamento de `GameScene`. Ha avisos de
acesso a shaders removidos pela otimizacao Dedicated Server; a verificacao de
login/personagem/mundo pelo cliente ainda deve confirmar o fluxo completo.
O validador da configuracao local tambem foi reexecutado: **Passed=12 Failed=0**.

## Dados de teste e preservacao

O teste da API criou uma conta e um personagem novos somente no banco isolado.
As credenciais de teste estao na pasta privada `C:\TOP-Restoration\private`;
elas nao foram impressas nem enviadas ao Cloudflare.

Nenhum `.request` antigo foi encontrado na copia ativa; o inventario vazio
foi preservado em `C:\TOP-Restoration\quarantine\requests-20261008`.
O staging antigo foi movido para `C:\TOP-Restoration\preserved\Build` antes
de gerar qualquer novo staging.

O segundo projeto em `E:\Tales of Pirates Unity` continua separado. Nenhum
arquivo ou historico dele foi integrado ao principal.

## Validacao executada nesta restauracao

- Unity **6000.4.4f1**: recompilacao batch concluida com codigo de saida **0**.
- `EditorServerConfigurationValidation.Run`: **Passed=12 Failed=0**.
- `LoginInteractionValidation.Run`: **Passed=17 Failed=0**.
- `WingAttachmentValidation.Run`: **Passed=1868 Failed=0**.
- Os novos relatorios desses tres validadores estao em `..\Tools`; os anteriores
  foram preservados em `C:\TOP-Restoration\preserved\validation-results-before`.
- API MariaDB local: autenticacao/JWT, criacao e leitura de personagem,
  equipamento e persistencia de item de asas verificados, inclusive apos reinicio.
- Cloudflare local: registro, login e consulta de conta verificados com D1 local.
- Launcher: selftest existente concluido.

O teste Unity de entrada no mundo/UDP foi posteriormente executado junto com o
smoke de ambiente: **14 PASS, zero FAIL**, com cliente sintetico em UDP 17892.
O resultado atual de mundo e desta restauracao, nao o relatorio antigo do backup.
A build de staging do cliente e o fluxo visual completo com login real ainda
estao pendentes.
Feche o Editor antes das validacoes batch; nao encerre processos a forca sem salvar.

Para cadastrar o projeto no Hub, use Ajouter / adicionar projeto do disco e
selecione `C:\Tales of Pirates Unity`, com Editor **6000.4.4f1**. Nao crie um
projeto novo nem selecione a pasta do backup.

## Sistema de ceu FS002

Instalado em `Assets\Resources\WorldEnvironment.asset`, com referencias aos
oito panoramas Fantasy Skybox. Menu do Editor: **Tools > World > Ceu e Tempo**;
menu do jogo: **F9**, somente admin. A previa e local, temporaria e nao exige
salvar/substituir a GameScene. Relogio/clima no multiplayer sao enviados pelo
servidor com o fuso do Windows do servidor, nao o fuso dos clientes.

Validacao desta implementacao: **34 verificacoes matematicas/visuais passaram**;
o smoke KCP confirmou envio de relogio/fuso, serializacao e rejeicao de alteracao
nao autenticada, junto com entrada no mundo e NavMesh. Nenhuma conta foi
autenticada nem dados foram escritos no banco nesse smoke. A alteracao global
por uma conta admin real ainda precisa de teste interativo.
Relatorios: `Tools\sky-validation-results.txt` e
`Tools\environment-network-results.txt`.

A build dedicada anterior ao ceu foi preservada em
`C:\TOP-Restoration\preserved\Build\GameProjectKG.editorserver-before-sky-20261009-000435`.
Somente o servidor local de teste foi autorizado para atualizacao; nao publicar
ou ativar o playit como parte dessa mudanca.

A nova build dedicada concluiu com **Succeeded | 0 errors | 00:05:13** e hash
publicado inalterado. A API local foi restabelecida (estava desligada) e o
servidor atualizado voltou a escutar UDP 7778; handshake KCP real passou.
O controle visual da camera foi corrigido no cliente/Editor: a camera antiga
usava fundo solido, agora usa skybox enquanto o sistema esta ativo e restaura
sua configuracao ao encerrar. Essa correcao tambem passou no smoke.

## Servidor compartilhado ativado com autorizacao

Em 09/10/2026 o usuario autorizou exposicao via tunel UDP para o colega.
Foi iniciada uma segunda instancia da build Dedicated Server em **7777**, com
`--api=https://gamekg.pages.dev`, sem alterar a instancia local 7778/MariaDB.
O script persistente e `Tools\Release\Start-SharedServer.ps1`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Tales of Pirates Unity\Tools\Release\Start-SharedServer.ps1"
```

O playit 1.0.10 foi ativado com copia privada da configuracao preservada em
`C:\TOP-Restoration\private\playit-shared\playit.toml`, sem colocar o segredo no
projeto/Git. O agente confirmou conta verificada e um tunel ativo. O handshake
KCP real passou tanto em `127.0.0.1:7777` como no endereco publico
`pgsql-henderson.tun.ply.gg:22538`; nao houve autenticacao ou saves nesses testes.
Nenhuma regra de firewall nova, deploy ou publicacao itch foi necessaria.

Os processos foram iniciados nesta sessao, nao como servicos automaticos.
Precisam continuar ligados para o colega conectar. O script de servidor nao
inicia o playit: ele deve ser iniciado separadamente com a configuracao privada.
API, dados e contas desta instancia sao Cloudflare/D1; nao sao o MariaDB local.
Jogadores reais podem alterar seus dados remotos ao jogar.
O Unity local ainda aponta para API local/7778: nao foi trocado silenciosamente.
Para ambos jogarem no compartilhado, configure ambos para Cloudflare e o
endereco publico, com os mesmos scripts/assets/mensagens Mirror.
O colega ainda precisa receber as fontes locais atualizadas; nao houve push.
