# Launcher - como funciona e como enviar updates

## Como funciona
1. O jogador baixa e executa um unico arquivo: `GameProjectKG-Launcher.exe`. O launcher e portatil e nao exige extrair uma pasta manualmente.
2. O launcher le `manifest.json` de `https://gamekg.pages.dev/patch/manifest.json`, com a lista de arquivos, tamanhos e hashes SHA-256.
3. Compara com a pasta de instalacao (padrao `C:\GameProjectKG\Game`, na raiz do disco do sistema; se a conta nao tiver permissao de escrita ali, usa `%LOCALAPPDATA%\GameProjectKG\Game`), baixa somente os arquivos novos ou alterados do GitHub Releases, confere os hashes, remove arquivos que sairam da build e repara arquivos corrompidos.
4. O botao **Verificar e reparar** valida o hash de todos os arquivos instalados e baixa novamente qualquer arquivo ausente ou corrompido. Use-o quando o jogo nao iniciar ou fechar inesperadamente.
5. Botao JOGAR abre o .exe do jogo. Antes de iniciar, o launcher verifica se ha uma atualizacao ou reparo pendente e impede a abertura ate a instalacao estar integra.

## Enviar um update (passo a passo)
1. No Unity, use **Tools > Build Game (Windows)**. A build e gerada em `Build\GameProjectKG`.
2. Abra um terminal na pasta `Launcher` e publique a build:
   ```powershell
   node tools\publish.js "..\Build\GameProjectKG" --notes "Novidades da versao"
   ```
   Tambem e possivel arrastar a pasta da build sobre `publicar-update.bat`. O publicador ignora arquivos de depuracao do Unity e envia ao GitHub Releases somente os arquivos novos ou alterados (identificados pelo hash).
3. O publicador atualiza `public\patch\manifest.json` no repositorio `kleberkunha1-eng/gamekg` e faz push para `main`. O GitHub Actions publica o site e o manifest no Cloudflare Pages automaticamente.
4. Quando o deploy termina, o launcher detecta a nova versao na proxima abertura. Nao e necessario manter o PC ligado para servir os arquivos: os downloads ficam no GitHub Releases.

O publicador requer `gh` autenticado com permissao de escrita no repositorio, Git e o arquivo local `publish.config.json` configurado para apontar ao clone de `kleberkunha1-eng/gamekg`. Esse arquivo e local e nao deve ser enviado ao repositorio.

## Atualizar o proprio launcher
Rode `npm run build` em `Launcher`. Gera `Launcher\dist\GameProjectKG-Launcher.exe`. Compacte em `.zip` e atualize os dois assets do release `launcher` (o site serve o `.zip` por padrao - baixar um `.zip` em vez do `.exe` cru reduz a chance do proprio navegador bloquear o download antes mesmo do Windows entrar em acao; o `.exe` continua disponivel em `/launcher.exe` para compatibilidade):
```powershell
Compress-Archive -Path "dist\GameProjectKG-Launcher.exe" -DestinationPath "dist\GameProjectKG-Launcher.zip" -Force
gh release upload launcher "dist\GameProjectKG-Launcher.exe" --repo kleberkunha1-eng/gamekg --clobber
gh release upload launcher "dist\GameProjectKG-Launcher.zip" --repo kleberkunha1-eng/gamekg --clobber
```
O site passa a oferecer o executavel atualizado. Os jogadores baixam o `.zip`, extraem e rodam o `.exe` de dentro.

## Endereco do servidor
`Launcher\launcher.config.json` define `patchUrl` e `siteUrl`, atualmente apontados para `https://gamekg.pages.dev`. O launcher passa a URL do site ao jogo; o cliente Unity usa a API nesse mesmo dominio.

## Teste automatico
`npm test` (no projeto do launcher) valida o fluxo local de publicacao, instalacao, atualizacao, remocao e reparo de arquivos.

## Observacoes
- Os dados do jogador (conta, personagens) ficam no servidor; atualizar o jogo nao apaga nada.
## O jogo e a API
O jogo nao acessa banco de dados: tudo passa pela API (`/api/game`). O launcher passa o endereco ao jogo (`--api=<siteUrl>`). O jogador so precisa do launcher.
