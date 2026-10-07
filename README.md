# Tales of Pirates Unity

> 📘 **Precisa gerar build, publicar no itch.io, ou gerenciar o servidor dedicado?** Veja o
> [Guia de Operações](GUIA-OPERACOES.md) — todos os comandos passo a passo.

Projeto configurado para Unity 6000.4.4f1. Abra `Assets/Scenes/LoginScene.unity` e entre em Play. Sem configuração de API, o Editor usa a API Node em `API` (porta 3000) e o MariaDB configurado nela. Para iniciar a API local: `cd API` e `node server.js`. Não execute uma segunda instância se a porta já estiver ocupada.

## Multiplayer

O cliente Unity usa Mirror/KCP e **nunca** inicia um host no computador do jogador. Após o login REST, ele lê o destino Mirror de `api.json` (ou dos argumentos `--game-server=HOST --game-server-port=PORT`) e conecta como cliente. Um `api.json` de produção deve conter:

```json
{
  "apiUrl": "https://sua-api.example",
  "gameServerHost": "game.example",
  "gameServerPort": 7777
}
```

Para testar direto no Unity Editor sem gerar/publicar um build, o Play Mode usa `Tools/server-address.txt` como destino do jogo quando os argumentos e o `api.json` do projeto não definirem host e porta. Com esse arquivo presente, o login usa a mesma API publicada do build para que o JWT seja aceito pelo servidor remoto. O arquivo local tem o formato `host:porta` e não é incluído no build. Um argumento `--api=URL` ou um `api.json` na raiz do projeto pode substituir a API publicada para testes locais.

Execute uma instância dedicada do mesmo build com `GameProjectKG.exe -batchmode -nographics --server --server-port=7777`. O servidor valida o JWT na API antes de liberar a seleção de personagem.

O cliente envia `ClientPing` a cada 3 segundos e recebe `ServerPong`, mantendo a sessão ativa abaixo do timeout KCP de 10 segundos.

A conexão inicial não marca o cliente como Ready: login e seleção não devem receber objetos do mundo. Após selecionar o personagem, o cliente carrega GameScene, prepara os objetos de cena e só então envia Ready. O servidor associa o jogador à conexão nesse momento e rejeita Ready antes da seleção. Os NavMeshAgents dos monstros ficam desativados nos prefabs e são ativados apenas em OnStartServer, após validar o NavMesh; clientes recebem movimento pela rede.

A tela de seleção solicita a lista de personagens assim que a sessão está conectada, sem esperar Ready. Esperar Ready nessa etapa impede a lista de carregar, pois esse estado depende da entrada no mundo. Criar `Tools/validate-character-list.request` em Edit Mode verifica essa condição sem build ou teste completo; resultado em `Tools/character-list-validation-results.txt`.

O TOPNetworkManager inicia explicitamente a autenticação em cada conexão, inclusive reconexões: o Mirror substitui seu próprio callback OnConnectedEvent durante StartClient, portanto o envio de JWT não depende de uma assinatura anterior nesse callback. As transições para seleção e mundo usam carregamento assíncrono para continuar processando a rede durante a troca de cena.

No cliente remoto, o JWT é enviado assim que conecta (o atraso de segurança permanece apenas no Host). A lista de personagens é solicitada assim que a autenticação é aceita, em paralelo ao carregamento da seleção; a resposta é preservada se chegar antes da tela e não se envia uma segunda solicitação enquanto a primeira está pendente. O login não inicializa o catálogo das janelas do jogo, o formulário de cadastro é criado somente ao abri-lo e o campo de conta/senha recebe foco automaticamente. **Tab / Shift+Tab** navegam pelos campos de login e cadastro. Ao entrar no mundo, a integração assíncrona de assets usa prioridade alta temporária, restaurada ao concluir; janelas não utilizadas são criadas somente ao abrir, por botão ou atalho, e reutilizadas. O navegador de depuração consulta o catálogo sem construir todas as janelas. O Console registra `[Loading]` com os tempos de resposta do login, lista, seleção, criação dos previews e carregamento da GameScene, separando espera da API/rede do carregamento local.

Criar `Tools/validate-world-entry.request` executa um teste isolado em Play na GameScene: um cliente sintético tenta Ready durante login (nenhum spawn permitido), recebe um personagem sintético e depois solicita Ready de mundo. O teste verifica a associação do jogador, entrega dos objetos e navegação exclusivamente no servidor. Não autentica contas nem grava personagens no banco; desliga o servidor de teste e restaura as cenas abertas ao terminar. Relatório: `Tools/world-entry-smoke-results.txt`.

### Hospedando o servidor na sua própria máquina (sem expor seu IP)

Abrir a porta do servidor direto no roteador expõe o IP residencial a varreduras constantes da internet. Em vez disso, usamos o [playit.gg](https://playit.gg) (gratuito, suporta UDP), que cria um túnel de **saída** da sua máquina — ninguém se conecta direto na sua rede, só no endereço público que o playit fornece.

1. Instale o playit.gg (https://playit.gg/download) — ele roda como serviço do Windows (`playitd`) com início automático e já fica autenticado após o setup inicial.
2. No painel (https://playit.gg/account): confirme o e-mail da conta e crie um túnel **UDP** apontando para a porta local `7777` (ou outra de sua escolha), associado ao agente desta máquina.
3. Copie o endereço público gerado (ex.: `algumacoisa.joinmc.link:25565`) para `Tools/server-address.txt` (1ª linha, formato `host:porta`; arquivo local, não versionado).
4. Gere a build (`Tools > Build Game (Windows)`) — o `api.json` da build passa a apontar `gameServerHost`/`gameServerPort` para esse endereço.
5. Rode `Tools/Release/Start-DedicatedServer.ps1` para iniciar o servidor dedicado com reinício automático em caso de queda (o túnel do playit roda à parte, como serviço).
6. Publique a build atualizada (itch.io via `Tools/Release/Publish-Itch.ps1`, por exemplo).

Se `Tools/server-address.txt` não existir, a build é gerada com `gameServerHost` vazio e o cliente mostra um erro claro em vez de tentar conectar a um servidor inexistente.

O cliente C++ original fornecido em `E:\NEW SV\Client` utiliza um protocolo TCP próprio, com handshake RSA/AES e Gate/Account/Game servers. Ele não é compatível com Mirror/KCP. Seus dados, animações e comportamento podem ser migrados para Unity, mas para conectar o Unity ao servidor legado seria necessária uma implementação completa e separada desse protocolo.

## Controles

- Clique no chão para andar; segure Shift para correr.
- Clique em um monstro para selecionar, aproximar e atacar.
- Botão direito e movimento do mouse giram a câmera; roda ajusta o zoom.
- Tecla 1: golpe poderoso; tecla 2: recuperação, com consumo de mana.
- Use o campo de chat para enviar mensagens. Morte provoca respawn após cinco segundos.

## Conteúdo e configuração

O terreno importado usa alturas e texturas de `E:\NEW SV\Client\map\garner.map`: uma região de 256 × 256 unidades, com 24 camadas de textura. A origem Unity corresponde à coordenada original (2218, 2782). Foram convertidos dois edifícios originais, suas texturas e a música de Argent. O jogador usa o modelo Lance existente; os inimigos usam o modelo animado disponível no prefab `Mob_Slime` (visualmente um cervo).

O arquivo de posicionamento `garner.obj` não foi encontrado no cliente fornecido. Os edifícios foram posicionados manualmente. O mapa inteiro de 4096 × 4096, todas as classes, quests, economia e demais sistemas de um MMO completo não estão certificados como concluídos. As transições de textura aproximam as máscaras originais usando Terrain splat weights.

## Verificação reproduzível

### Janelas e asas

Mage Wings inclui ajustes para as quatro raças na build: Lance, Carsise e Phyllis usam posição `(0.06, -0.18, -15.21)` e escala 60; Ami usa `(0.05, -0.17, -12.70)` e escala 50. Todas usam rotação zero. Ajustes locais substituem apenas a combinação raça/item correspondente, sem apagar os padrões publicados das outras raças. Um arquivo local inválido é registrado como erro e os padrões da build são preservados.

As janelas do jogo podem ser arrastadas pelo fundo, sem impedir o arraste de itens ou o uso de controles. As telas de seleção/criação que fixam sua posição continuam fixas.

As asas originais usam o efeito indicado na coluna 90 de `iteminfo.txt`, não o modelo genérico `10130005`. `Tools > PKO > Build Animated Wings` gera os visuais em `Assets/Resources/Wings`, usando as animações esqueléticas embutidas nos modelos originais. Antes de regenerar, exporte os modelos necessários com `PKOAssetBatchExporter <cliente> <Assets/ImportedClient> --wing-skinned <modelos>`.

Mage Wings (item `990001`) usa o modelo Emperor e a animação original `Asas_Penas_Vento_Loop` (~8 segundos), incluindo penas e rastros de vento. O FBX e a textura estão em `Assets/Resources/Wings/Emperor`. `Tools > PKO > Replace Mage Wings with Emperor` atualiza o mesmo prefab, preservando sua largura e centro anteriores; o encaixe e a largura proporcional ao personagem permanecem inalterados. Também pode ser acionado por `Tools/replace-mage-wings.request`, com resultado em `Tools/replace-mage-wings-results.txt`. A reconstrução geral de asas usa essa mesma fonte, sem restaurar a antiga animação Rebirth.

`Tools > PKO > Validate Windows and Wings` verifica arraste, texturas da interface, visuais equipáveis e deformação animada, sem gravar dados de contas. O relatório fica em `Tools/visual-validation-results.txt`.

As asas originais usam o dummy indicado na coluna 90 de `iteminfo` (dummy 24 para as 33 asas importadas), como `Character.cpp` / `EffectObj.cpp` do cliente original. Os prefabs preservam posição, rotação, escala, materiais e animações de cada camada dos efeitos originais; não são normalizados para uma largura comum. A Mage Wings personalizada permanece no osso do peito (`Spine1`), com largura base em 90% da altura do esqueleto e ajuste aprovado para a raça 2: posição `(0.06, -0.18, -15.21)`, rotação zero e escala 60. No jogo, **F8** abre o ajuste de asas: posição XYZ proporcional à altura, rotação XYZ e escala uniforme. Posição e rotação usam passos de **0,01** (graus na rotação). **Escala** aceita valor digitado ou botões **-/+** em passos de 5%; **1** corresponde a 100% do tamanho base, com intervalo de 0,1 a **150000 (150 mil)**. Os campos mostram decimais sem zeros extras e aceitam ponto ou vírgula decimal. A escala não desloca o encaixe nem modifica a animação. Os valores são separados por raça e item. **Restaurar esta asa** zera posição/rotação e retorna escala a 1. **Salvar** persiste em `WingPose.json` no diretório de dados do jogador e, no Editor, também em `Assets/Resources/PkoChar/WingPose.json`; **Copiar dados** inclui a escala no JSON para enviar na conversa. Arquivos antigos sem escala preservam o tamanho base. São ajustes visuais locais, sem alterar dados do servidor. Cliques no painel não movem o jogador.

### Atalhos e chat

Os atalhos são lidos dos formulários do cliente original, uma vez por janela (os aliases por arquivo não duplicam o acionamento):

| Atalho | Janela |
|---|---|
| Alt+E | Inventário |
| Alt+A | Atributos |
| Alt+S | Habilidades |
| Alt+Q / Alt+J | Missões (diário funcional) |
| Alt+W | Mapa |
| Alt+R | Busca |
| Alt+P | Grupo |
| Alt+C / Alt+G | Guilda (painel funcional) |
| Alt+F / Alt+N | Amigos (painel funcional) |
| Alt+M | Correio |
| Alt+Y | Comércio |
| Alt+H | Ajuda |
| Alt+O | Sistema |
| Alt+Z | Informações de NPC |
| Alt+X | Informações do personagem |

Alt+B aciona o botão de bloqueio do inventário; o bloqueio por senha ainda não possui implementação no servidor e informa essa limitação, sem simular proteção. Alt+P prioriza a janela de grupo, não a janela GM de depuração. F1–F10 e 1–0 acionam a barra; o navegador de janelas de depuração usa Ctrl+Shift+F10. Atalhos da barra são ignorados enquanto se digita.

Os botões de guilda, amigos, missões e grupo do HUD e seus atalhos abrem os painéis ligados ao jogador, não os layouts vazios importados. As teclas simples G/N/M/J/Y continuam disponíveis, além das combinações Alt. Todas as janelas importadas e os painéis funcionais, incluindo F8/F9/F10 e o navegador de depuração, têm **X**. Fechar comércio cancela a negociação; fechar convites recusa; fechar F9 libera a câmera. Os cliques nos painéis não movem o personagem. Botões de layouts cujo recurso ainda não existe no cliente informam explicitamente a limitação; abrir o layout não implica que a funcionalidade de servidor esteja implementada.

O F10 mantém a geração pendente até a confirmação do servidor, bloqueando pedidos duplicados. Cada pedido possui identificador e respostas atrasadas de outros pedidos são ignoradas; os comandos antigos permanecem compatíveis. O servidor informa item inválido, quantidade inválida, sessão expirada, autorização não confirmada ou inventário cheio (inclusive entrega parcial), sem informar sucesso antes de adicionar os itens. Sem resposta por 25 segundos, o painel informa que a geração não foi confirmada. As permissões continuam sendo verificadas pela API com o token da conexão do jogador.

Regressões de interface são verificadas no Editor por **Tools > PKO > Validate Window Controls** e **Validate Login Interaction**. Para validar geração pela rede, crie `Tools/validate-admin-generation.request` com o Editor fora do Play: o teste usa KCP local, personagem sintético e API local temporária, valida autorização/negação, refino, sessão ausente e entrega parcial, sem gravar contas reais. O resultado fica em `Tools/world-entry-smoke-results.txt`; cenas e configuração de API são restauradas ao terminar. Esta validação isolada não substitui o teste com uma conta real no servidor publicado.

Enter foca/envia o chat. O canal Local é o padrão: o servidor entrega a mensagem apenas a jogadores do mesmo mapa em até 25 unidades, incluindo o remetente. A mesma mensagem aparece no histórico local e acima da cabeça por 4–10 segundos. Mundo, grupo, guilda e os comandos `/p`, `/w nome`, `/s` e `/invite nome` passam pela conexão multiplayer, não por uma simulação local. A guilda conserva a mensagem de indisponibilidade do servidor quando não implementada.

A mensagem de rede do chat mudou; cliente e servidor dedicado precisam ser reconstruídos juntos. Clientes publicados anteriormente precisam de uma nova build para usar esse protocolo.

### Salvamento automático

O servidor salva os personagens periodicamente (30 segundos na cena de login), além de salvar na desconexão. O ciclo usa uma cópia da lista de conexões e não inicia outro ciclo enquanto o anterior está pendente. Falhas de persistência aparecem no log.

`Tools > Autosave > Enabled` vem ativado por padrão no Editor. A cada 60 segundos, antes de entrar em Play e ao fechar, salva assets e cenas nomeadas fora do Play Mode. Antes de salvar cada cena alterada, cria uma cópia recuperável em `Library/TOPAutosave`, mantendo as últimas dez por cena. Cenas sem nome recebem apenas uma cópia de recuperação, sem substituir outra cena. Alterações de objetos durante Play Mode não são gravadas nas cenas. Use `Tools > Autosave > Open Recovery Folder` para localizar as cópias.

`Tools > PKO > Validate Multiplayer Transport` verifica o handshake KCP e troca pings Mirror confiáveis por 12 segundos, tanto localmente quanto pelo destino público, sem alterar contas. Exige pelo menos 10 respostas e nenhuma desconexão; o relatório registra a duração e a quantidade de respostas. Uma abertura de conexão bem-sucedida, sozinha, não comprova que o tráfego continua funcionando além do timeout de 10 segundos. Isso não substitui testes autenticados com dois jogadores nem comprova gravação no banco.

A geração de builds usa uma pasta intermediária e só substitui a build ativa após sucesso. Erros de compilação preservam a build anterior; uma cópia anterior recebe o sufixo `.previous.<data>`.

Última execução: 3 de outubro de 2026, 16 verificações aprovadas, sem erros capturados. Compilação no editor aprovada; nenhuma build standalone foi validada. Caminhada/corrida, dano, mana, XP, evolução, respawn, minimapa e integridade/altura do modelo foram exercitados.

No editor, use `TOP > Validate gameplay (local test)` para executar o teste isolado do mundo. Ele usa um personagem sintético e não grava contas/personagens no banco. Os resultados ficam em `Tools/smoke-results.txt`, erros em `Tools/smoke-errors.txt` e a captura em `Tools/gameplay-smoke.png`. Esse teste não substitui testes de login real nem de múltiplos clientes remotos.

`TOP > Configure playable world` reconstrói a configuração de cena/prefabs e o terreno. Salve cenas abertas antes de executá-lo. Essa ferramenta aplica posições e parâmetros definidos em `Assets/Editor/PlayableWorldSetup.cs`.

## Limpeza e origem

581 assets candidatos sem dependência das cenas/Resources, além de exemplos e cópias antigas, foram arquivados fora do projeto em `C:\Users\klebe\Tales of Pirates Unity Backups\20261003`. Sete dependências necessárias dos monstros foram restauradas durante a validação. Também foram removidas 12 dependências de pacotes sem uso identificado (lista em `Tools/removed-unused-packages.txt`). Os relatórios em `Tools` registram a seleção; o cliente original foi preservado.

A conversão de modelos usa `Tools/ConvertClientModel.cs` e estruturas do projeto [WeaponOwl/PKO-file-viewer](https://github.com/WeaponOwl/PKO-file-viewer). Fontes de referência do formato de mapa estão em `Tools/OriginalFormat`; o manifesto dos arquivos originais importados está em `Tools/import-manifest.json`.
