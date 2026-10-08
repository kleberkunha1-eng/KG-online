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

Para testar direto no Unity Editor sem gerar/publicar um build, o Play Mode usa `Tools/editor-server-address.txt` quando presente; caso contrario, usa `Tools/server-address.txt`. Argumentos e o `api.json` do projeto continuam tendo prioridade. Nesta maquina, o arquivo exclusivo do Editor aponta para `127.0.0.1:7777`: acessa o servidor dedicado diretamente, sem depender do tunel publico para testes no mesmo computador. Nao inicia um host nem troca de servidor silenciosamente. Para testar o tunel no Play Mode, renomeie temporariamente o arquivo exclusivo do Editor. Os arquivos tem formato `host:porta` e nao sao incluidos no build; `Tools/server-address.txt` continua sendo o destino dos clientes publicados. Com um desses arquivos presente, o login usa a mesma API publicada do build para que o JWT seja aceito pelo servidor. Um argumento `--api=URL` ou um `api.json` na raiz do projeto pode substituir a API publicada para testes locais.

Se o transporte falhar, o login mostra o destino e libera uma nova tentativa em vez de permanecer bloqueado em "Conectando". O timeout KCP nao foi aumentado: autenticar na API nao comprova que o UDP do servidor ou o tunel esteja disponivel. `Tools > PKO > Validate Multiplayer Transport` continua verificando o destino PUBLICO, ignorando o arquivo exclusivo do Editor, e exige pelo menos 10 respostas em 12 segundos. Use tambem `Tools > PKO > Validate Editor Server Configuration` para verificar a separacao dos destinos.

Se o teste funcionar fora do Unity, mas falhar no Editor, verifique as regras do executavel exato do Unity e o perfil ativo em `Get-NetConnectionProfile`: uma regra explicita de **Block** tem prioridade sobre **Allow**. Foi identificado um bloqueio Public para Unity 6000.4.4f1, enquanto sua permissao existente era apenas Domain. `Tools/Release/Repair-UnityGameFirewall.ps1`, executado pelo administrador, permite somente respostas UDP nas portas local/publica do jogo para esse executavel e preserva o bloqueio TCP. Nao desliga o firewall, nao se eleva automaticamente e nao altera regras de outras versoes do Unity. `-WhatIf` mostra a alteracao sem aplica-la; depois de aplicar, repita a validacao de transporte por 12 segundos. A correcao do codigo nao substitui essa permissao do Windows.

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

### Novas estruturas

Os 75 modelos GLB de `SEND TO PROJECT` ficam em `Assets/Novas estruturas`, preservando nomes e subpastas, inclusive `new character`. O pacote oficial Unity glTFast importa malhas, materiais, texturas incorporadas e animacoes. Arraste o asset GLB da janela Project para a cena para criar uma instancia; expanda o asset para acessar seus componentes importados. Os arquivos originais nao foram modificados e nenhuma estrutura foi posicionada automaticamente no mapa.

Esses assets ficam fora de `Resources`: somente os modelos referenciados pelo jogo devem entrar na build, evitando incluir toda a colecao de aproximadamente 6,9 GB sem necessidade. A importacao nao gera colisores nem NavMesh automaticamente; configure-os conforme o uso de cada estrutura no novo mapa. Execute `Tools > Validate New Structures` para verificar malhas, materiais, texturas e contar as animacoes; o resultado fica em `Tools/new-structures-validation-results.txt`.

O terreno importado usa alturas e texturas de `E:\NEW SV\Client\map\garner.map`: uma região de 256 × 256 unidades, com 24 camadas de textura. A origem Unity corresponde à coordenada original (2218, 2782). Foram convertidos dois edifícios originais, suas texturas e a música de Argent. O jogador usa o modelo Lance existente; os inimigos usam o modelo animado disponível no prefab `Mob_Slime` (visualmente um cervo).

O arquivo de posicionamento `garner.obj` não foi encontrado no cliente fornecido. Os edifícios foram posicionados manualmente. O mapa inteiro de 4096 × 4096, todas as classes, quests, economia e demais sistemas de um MMO completo não estão certificados como concluídos. As transições de textura aproximam as máscaras originais usando Terrain splat weights.

## Verificação reproduzível

### NewCharacterTest

`NewCharacterTest` e a quinta opcao de personagem (ID 4 no campo historico `Job`, usado pelo fluxo como raca). Nao e uma nova profissao de combate: herda Lance (ID 0), sem substituir personagens existentes. O modelo vem de `Assets/ImportedClient/NewCharacterTest/Personagem_RPG.glb`. O rig `Resources/PkoChar/Rig_0004` e o esqueleto exportado do Blender, com tamanho (1,70 m), proporcoes, pesos e as 72 animacoes `0000_action*` exatamente como no GLB, convertidas para clips legacy em `Resources/PkoChar/NewCharacterTest/Animations` (eventos e wrap mode seguem o clip do Lance de mesmo nome). Os ossos `RPG_*` dos dedos viram ossos extras (indice 56+) e os nubs ausentes dos dedos do pe recebem marcadores vazios. As cinco partes ficam em `Resources/PkoChar/NewCharacterTest`, mantendo substituicao por equipamento, rosto e cabelo. Pecas feitas para o Lance (rosto, cabelo, armaduras) sao reposicionadas por `PkoRigFit`: cada peca e escalada em torno da junta do Lance e colocada na mesma junta deste rig. O skinning do personagem usa 4 ossos por vertice. Nas animacoes com joelhos e pes muito dobrados (`0000_action3`, `0000_action42`) a barra da bota estica do mesmo jeito que no Blender.

Criacao, selecao, aparencia sincronizada, restricoes de equipamento, salao, armas, asas e Blue Mage Set usam a identidade nova com compatibilidade Lance. Os atributos iniciais nas duas APIs sao os mesmos do Lance, incluindo HP 150, MP 50 e SP 100; nao e necessaria uma migracao de esquema. Para testar na API publicada, publique tambem a funcao Cloudflare atualizada e use um servidor/cliente atualizados: modificar somente o Editor nao atualiza servicos em execucao.

O rosto/cabelo padrao preserva a cabeca completa do modelo fornecido. Ao escolher outro rosto, penteado ou capacete original, a cabeca/cabelo passa a usar as variantes do Lance, pois o GLB tem cabelo integrado na mesma malha do rosto; isso evita sobrepor dois penteados. Corpo, luvas e botas continuam usando as partes novas ate serem substituidos por equipamento. A validacao exige p99 de alongamento das arestas menor que 4 na adaptacao e pelo menos 99% das arestas abaixo de 4x em cada amostra animada, alem das previas visuais.

`node Tools/Extract-NewCharacterTextures.cjs` extrai as tres texturas originais sem alterar o GLB; o bake usa copias importadas com compressao, mipmaps e limite 4096 para nao carregar as texturas enormes de autoria no jogo. `Tools > PKO > Build NewCharacterTest` reconstroi os assets e verifica cada clip original em tres amostras, as cinco racas e os equipamentos, escrevendo `Tools/new-character-test-results.txt` e previews. `node --test Tools/tests/new-character-api.test.cjs` verifica criacao, lista e selecao nas duas APIs, com bancos isolados em memoria. Criar `Tools/validate-new-character-gameplay.request` executa entrada no mundo e movimento reais por KCP isolado, sem autenticar contas nem gravar no banco, e restaura as cenas abertas; resultado em `Tools/new-character-gameplay-results.txt`.

### Blue Mage Set

Os cinco equipamentos personalizados usam IDs reservados, nível 10 e aceitam todas as raças/classes:

| ID | Item | Slot | Referência de atributos |
| --- | --- | --- | --- |
| 990010 | Blue Mage Helm | Capacete | Mousey Cap (2202) |
| 990011 | Blue Mage Chestplate | Armadura | Medic Robe (365) |
| 990012 | Blue Mage Gloves | Luvas | Medic Gloves (541) |
| 990013 | Blue Mage Pants | Cinto | Visual sem bônus adicionais |
| 990014 | Blue Mage Boots | Botas | Medic Boots (717) |

As calças ocupam o slot de cinto existente, sem acrescentar slots ao protocolo. O F10 usa o mesmo catálogo que inventário e equipamento, incluindo a categoria `Cinto / Calcas`. Os ícones de 256×256 são renderizações dos modelos texturizados, não recortes do atlas de textura. Os modelos fornecidos em OBJ são estáticos: a preparação limita cada peça a 22 mil triângulos (10 mil nos assets atuais), preserva UVs e reduz as texturas a 2048×2048. O bake cria uma versão com pesos e bind poses para cada raça a partir das partes originais; as peças acompanham o esqueleto também durante voo. Um traje interno azul ajustado preenche as aberturas entre peças e substitui apenas a região correspondente da roupa original, evitando roupa antiga atravessando a armadura ou buracos no corpo. Aparências originais têm prioridade sobre as peças correspondentes.

O encaixe preserva a frente +Z do modelo e usa as regiões anatômicas do esqueleto de cada raça. As luvas incluem os antebraços e são alinhadas ao pulso/cotovelo; as botas incluem as canelas e são alinhadas ao tornozelo/joelho. O bake ajusta a folga radial das peças ao traje interno em faixas ao longo de cada membro e do tronco, preservando os detalhes externos e evitando que o corpo atravesse a armadura. O traje azul inclui uma gola fechada até a região da cabeça e mangas/perneiras fechadas dimensionadas pelas articulações, incorporadas ao mesmo mesh/material, sem renderers adicionais. As perneiras substituem a geometria inferior da roupa original que poderia unir e esticar entre as duas pernas. As calças usam pesos anatômicos de quadril, coxa, joelho e tornozelo, mantendo o painel central preso à pelve; não recebem influência de braços nem da perna oposta. O elmo fechado oculta o rosto e o cabelo originais enquanto equipado; removê-lo restaura a aparência escolhida do personagem.

Os atributos base persistidos são separados dos bônus dos equipamentos. Ao entrar no mundo, o servidor restaura os atributos base, reaplica os bônus das peças/refinos/gemas e só então limita HP/MP/SP aos máximos calculados. Trocas e remoções recalculam o conjunto equipado inteiro, sem subtrair bônus que não foram aplicados no login. Personagens salvos com HP zero retomam o respawn normal, evitando ficar permanentemente sem movimento ao reconectar. A validação KCP de geração inclui HP sincronizado, recarga dos equipamentos, ausência de acúmulo de bônus e movimento real antes/depois do respawn.

O inventário pertence ao personagem, não à conta: as APIs consultam e salvam por `character_id`. Ao sair do mundo, as janelas de gameplay são removidas do cache persistente com seus callbacks; o próximo personagem recebe novas janelas. O HUD acompanha a identidade do jogador local e limpa ícones ao desconectar, sem reaproveitar referências ao personagem anterior. A configuração repetida de áudio reutiliza os componentes de clique/Graphic em vez de interromper a inicialização do inventário. O carregamento de inventário vazio/nulo também limpa itens anteriores. `Tools/validate-window-controls.request` verifica áudio repetido, troca de inventários, limpeza e Alt+E entre sessões.

Para regenerar a partir dos arquivos Meshy, execute o Blender em background com `Tools/Prepare-BlueMageSet.py -- --source "<pasta BLUE SET>" --project "<projeto>"` e depois `Tools > PKO > Build Blue Mage Set` no Unity. A geometria intermediária fica em `Assets/ImportedClient/BlueMageSet`, os assets de jogo em `Assets/Resources/PkoChar/BlueMageSet` e os ícones em `Assets/Resources/PKOUI/icon`. O builder valida catálogo, slots, ícones, texturas, orçamento de triângulos, pesos, deformação e equipamento/remoção nas quatro raças, incluindo ocultação/restauração do rosto. A cobertura é medida em três anéis de 24 direções por região: tronco, pescoço, coxas, canelas e antebraços, na pose inicial e no meio das animações de espera, corrida e ataque terrestres/aéreas. A medição usa os meshes deformados e as posições atuais dos ossos, isolando a região anatômica testada. As prévias de frente, costas e ambos os lados incluem a pose de bind e amostras de espera, corrida e ataque no chão e em voo, sem as asas cobrindo a armadura. Resultado em `Tools/blue-mage-set-results.txt`, com prévias por raça no mesmo diretório.

### Janelas e asas

Mage Wings inclui ajustes para as quatro raças na build: Lance, Carsise e Phyllis usam posição `(0.06, -0.18, -15.21)` e escala 60; Ami usa `(0.05, -0.17, -12.70)` e escala 50. Todas usam rotação zero. Ajustes locais substituem apenas a combinação raça/item correspondente, sem apagar os padrões publicados das outras raças. Um arquivo local inválido é registrado como erro e os padrões da build são preservados.

Mage Wings e Rebirth Wings ativam as poses de voo originais do personagem: espera 42, movimento 43, pose 44 e sentado 45. Espera e movimento em combate também usam voo; ataques, habilidades e morte mantêm suas ações. Os ciclos de espera, movimento e sentado compartilham a fase da animação da asa, preservando a duração do loop da asa e evitando deriva entre as duas animações. Remover a asa restaura as poses terrestres, sem alterar a posição de rede ou a colisão do jogador.

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

## Trace de desenvolvimento

Ferramenta interna da equipe (não aparece para o jogador). Registra travadas, requisições e tráfego do multiplayer, para decidir o que otimizar ou remover.

- **Unity** (`Assets/scripts/Core/GameTrace.cs`): liga sozinho no Editor, em builds de desenvolvimento e no servidor (`--server`). Na build de release do jogador fica desligado, a menos que se use `--trace`. `--no-trace` desliga.
- **O que registra:**
  - `FREEZE`/`HITCH`: frames acima de 1 s ou de 100 ms.
  - `STALL`: thread principal travada, detectada por outra thread, com o trecho de código aberto no momento.
  - `SLOW`: blocos medidos com `GameTrace.Measure` acima de 50 ms.
  - `HANDLER`: mensagens Mirror acima de 20 ms.
  - `NET`: mensagens acima de 16 KB.
  - `HTTP`/`HTTP-SLOW`: chamadas à API.
  - Logs, erros e exceções, com mensagens repetidas agrupadas.
  - `MARK`: momentos importantes.
  - `SUMMARY`: a cada 10 s, com fps, GC, memória, rtt, tráfego por tipo de mensagem e HTTP.
- **API** (`API/trace.js`): para cada request, registra tempo, status, número de queries e tempo de SQL (`REQ`, `REQ-SLOW` acima de 500 ms, `REQ-ERR`). Também registra queries acima de 50 ms (`SQL-SLOW`) e travas do event loop. Não grava corpo, parâmetros nem tokens. Desliga com `API_TRACE=0`.
- **Arquivos:**
  - `Logs/Trace` (Editor);
  - `<build>/Trace` (builds/servidor);
  - `API/logs` (API).

  Cada arquivo tem no máximo 20 MB e é dividido ao atingir esse tamanho. São mantidos até 40 arquivos ou 200 MB na Unity, e até 10 arquivos na API.
- **Menu `Tools > Trace`:**
  - **Ver Trace** abre a janela com filtros (Problemas, Tudo, Resumo, Rede/HTTP, Logs), busca e as 5 piores ocorrências.
  - **Apagar Trace** remove todos os traces. O arquivo em uso é esvaziado.
  - **Abrir Pasta** abre a pasta dos traces.
- `--trace-profiler` (ou `TOP_TRACE_PROFILER=1`) grava também uma captura `.raw` do Unity Profiler.

## Limpeza e origem

581 assets candidatos sem dependência das cenas/Resources, além de exemplos e cópias antigas, foram arquivados fora do projeto em `C:\Users\klebe\Tales of Pirates Unity Backups\20261003`. Sete dependências necessárias dos monstros foram restauradas durante a validação. Também foram removidas 12 dependências de pacotes sem uso identificado (lista em `Tools/removed-unused-packages.txt`). Os relatórios em `Tools` registram a seleção; o cliente original foi preservado.

A conversão de modelos usa `Tools/ConvertClientModel.cs` e estruturas do projeto [WeaponOwl/PKO-file-viewer](https://github.com/WeaponOwl/PKO-file-viewer). Fontes de referência do formato de mapa estão em `Tools/OriginalFormat`; o manifesto dos arquivos originais importados está em `Tools/import-manifest.json`.
