# Tales of Pirates Unity

> 📘 **Precisa gerar build, publicar no itch.io, ou gerenciar o servidor dedicado?** Veja o
> [Guia de Operações](GUIA-OPERACOES.md) — todos os comandos passo a passo.

Projeto configurado para Unity 6000.4.4f1. Abra `Assets/Scenes/LoginScene.unity` e entre em Play. Sem configuração de API, o Editor usa a API Node em `API` (porta 3000) e o MariaDB configurado nela. Para iniciar a API local: `cd API` e `node server.js`. Não execute uma segunda instância se a porta já estiver ocupada.

## Colaboracao pelo GitHub

O repositorio compartilhado e `https://github.com/kleberkunha1-eng/KG-online`.
Cada colaborador precisa de permissao de escrita; aparecer na lista de
contribuidores, por si so, nao concede essa permissao.

Git nao sincroniza automaticamente ao salvar: as alteracoes sao enviadas por
**commit + push** e recebidas por **pull**. Antes de editar, salve/commit suas
alteracoes e atualize sua branch. Use uma branch por tarefa e um pull request
para integrar na branch principal. Evitem editar a mesma cena/prefab em paralelo.
Nao use OneDrive ou uma pasta de rede para compartilhar o projeto Unity aberto.

Instale Git e Git LFS antes de clonar:

```powershell
git lfs install
git clone https://github.com/kleberkunha1-eng/KG-online.git
Set-Location KG-online
git lfs pull
git switch -c minha-tarefa
```

Abra a copia pelo Hub com Unity **6000.4.4f1**. Versione scripts, assets e seus
`.meta`, `Packages` e `ProjectSettings`. Nao compartilhe `Library`, `Temp`,
`node_modules`, builds, dumps de banco, `.env`, `Secrets` ou tokens de acesso.
Os caches sao regenerados; dependencias Node usam os manifests/lockfiles.
Cada maquina precisa de configuracao e credenciais locais proprias.
Para um colaborador usar a API e o multiplayer publicados, execute
`Configurar-Unity.cmd` na raiz da copia clonada, com o Editor fechado.
Ele baixa os objetos Git LFS disponiveis, verifica a API e o DNS, preserva
qualquer `api.json` anterior em `UserSettings/CollaboratorSetup`, grava os
endpoints publicados e abre Unity 6000.4.4f1. Se o Editor nao estiver instalado,
abre o Hub na instalacao da versao exata; conclua a instalacao e execute novamente.
Instale Git for Windows/Git LFS e Unity Hub antes. Login/licenca Unity exigem
interacao do usuario. Uma instalacao personalizada pode ser informada:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Setup\Configure-Collaborator.ps1 -UnityExe "D:\Unity\6000.4.4f1\Editor\Unity.exe"
```

O configurador nao cria banco/API local, nao abre firewall/tunel e nao publica
no itch. Ele usa `https://gamekg.pages.dev` e `pgsql-henderson.tun.ply.gg:22538`.
O servidor publicado deve estar ativo e aceitar a identidade dessa API;
HTTP/DNS acessiveis nao garantem conexao UDP nem entrada no mundo.
Mudancas futuras do endereco do tunel exigem atualizar o configurador.
O servidor local de teste `127.0.0.1:7778` nao e acessivel do computador do
colega, e nao e iniciado por esse procedimento.
O anfitriao pode usar `Tools/Release/Start-SharedServer.ps1` para a instancia
dedicada **7777** autenticada pelo Cloudflare. Esse script exige uma build
Dedicated Server existente e nao inicia o playit. O tunel deve estar ativo
separadamente no anfitriao. Login HTTP 200 seguido de timeout KCP indica que
a API respondeu, mas nao confirma acesso ao servidor UDP.
Ambos os clientes precisam dos mesmos scripts/mensagens Mirror que o servidor;
clonar a versao antiga do GitHub nao entrega as mudancas locais ainda sem push.
Para a restauracao e servidor dedicado local, consulte
[API/RESTORACAO-LOCAL.md](API/RESTORACAO-LOCAL.md); os caminhos de ferramentas
nesse ambiente podem precisar de ajuste em outra maquina.

Modelos/texturas binarios e dados grandes de terreno usam as regras de Git LFS
em `.gitattributes`. Isso e necessario para assets acima do limite de 100 MiB
do Git normal. O armazenamento/trafego LFS depende da cota da conta GitHub.
Essa preparacao nao envia os assets existentes: os arquivos ja rastreados
precisam ser renormalizados e revisados no index antes do primeiro commit com LFS.
Nao reescreva o historico compartilhado nem use force-push para fazer essa conversao.

Antes do primeiro envio desta copia restaurada, revise tambem os arquivos
ignorados: ignorado nao significa dispensavel. Assets necessarios devem entrar
com seus `.meta`; referencias/backups, configuracoes privadas e dados pessoais
devem permanecer separados. A pasta `.plastic` era rastreada e ainda precisa
ser retirada apenas do index numa alteracao aprovada, preservando a copia local.
Nao use `git add .` indiscriminadamente nem envie as credenciais de producao.

## Multiplayer

### Interacao entre jogadores (primeira etapa da importacao)

Clique com o botao direito em outro personagem para abrir o menu compacto:
Trade, convite para Party e desafio de duelo. Arrastar o botao direito continua
girando a camera. Trade e Party reutilizam os componentes existentes.
O painel de Trade permite selecionar itens nao equipados, informar quantidade,
retirar itens da oferta e definir ouro. Ambos precisam travar a oferta.
Trocas preservam refino, durabilidade e gemas; alteracoes nos itens oferecidos,
falta de espaco/ouro, morte, mapa diferente ou distancia acima de 5 metros
cancelam a negociacao sem transferencia parcial.

Duelo precisa de aceite do outro jogador em ate 30 segundos. Ambos precisam
estar vivos, no mesmo mapa, a ate 20 metros e fora de uma negociacao.
Apenas o adversario consentido recebe ataques/skills individuais; ataques
comuns continuam seguindo alcance e cooldown. O duelo termina com 1 HP
(sem morte, itens ou recompensas), cancelamento, desconexao, troca de mapa
ou afastamento. Skills individuais e em area revalidam a permissao de dano
por alvo; um alvo com varios colliders recebe dano apenas uma vez por cast.
Esta é uma adaptação local do desafio individual do cliente original. O combate PK aberto e as regras nativas por mapa estão implementados conforme documentado no fim deste README; entrada, instâncias e cenário original da arena teampk ainda não foram importados.

Alteracoes no PlayerCombat incluem novos comandos/SyncVars Mirror: clientes
e servidor precisam de builds da mesma revisao antes de jogar juntos.
Nao conecte o Editor modificado ao servidor antigo para testar estes comandos.
QuestTable preserva os IDs inferidos antigos e acrescenta o catalogo Lua nativo
em um namespace separado. A importacao parcial, flags, registros e cadeias
estao documentados em "Catalogo Lua nativo, flags e episodios" abaixo.

Validacao isolada (Editor fechado; nenhuma conta/banco real e usado):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" -batchmode -projectPath "C:\Tales of Pirates Unity" -executeMethod TOPWorldEntrySmokeRunner.RunSocialBatch -logFile "social-validation.log"
```

O runner encerra Unity com codigo 0/1 e escreve
`Tools/social-gameplay-results.txt`. Ele usa UDP 17892 e duas conexoes KCP
sinteticas para validar autoridade, combate/skills, aceite/recusa/expiracao,
desconexao, party, atributos de itens e transferencia de ouro, junto dos
testes existentes de terreno, movimento e camera. Nao valida persistencia
real na API nem uma sessao entre dois computadores.

### NPCs, lojas e receitas importadas

Ao clicar em um NPC proximo, o personagem se aproxima automaticamente e abre
a interacao a ate 3 metros. Clicar no chao cancela essa aproximacao.
Dialogos e lojas usam painel opaco com rolagem. Compras, vendas, receitas e cura
revalidam no servidor o jogador que enviou o pedido, NPC aberto, mapa, distancia,
vida e ausencia de duelo/trade.

A GameScene usa os 55 modelos originais de NPC ja presentes no mapa, sem
capsulas/esferas visuais substitutas. Os modelos, materiais, colliders e labels
originais foram ligados aos componentes de servico existentes, preservando
configuracoes, IDs de quests e NetworkIdentity.sceneId. O Jackpot Machine (154)
fica inativo porque sua colocacao esta comentada no Garner original; seu objeto
foi preservado, nao excluido.

`TOP/World/Bind Services to Existing Original NPCs` valida correspondencia exata
por ID/nome/modelo antes de salvar e cria backup em `Library/TOPAutosave/NpcBindings`.
O resultado fica em `Tools/npc-visual-bindings-results.txt`. Os populadores
preservam servicos configurados e nao recriam os modelos ja ligados.

O cursor usa os frames originais `mouseon.ani` (mao aberta) e `attack.ani`
(espada), extraidos por `Tools/import-original-cursors.ps1`. Hover sobre NPCs
e objetos interativos usa a mao; monstros vivos, adversarios de duelo aceito
e jogadores atacaveis em teampk usam a espada. Jogadores protegidos usam a mao
para interacao social. Chao, o proprio personagem e monstros mortos usam o
cursor normal. Janelas bloqueiam hover/ataques no mundo por tras delas.
O cliente usa os campos sincronizados para classificar jogadores remotos,
nao o estado de inicializacao exclusivo do servidor.

Foram importados os
estoques de Goldie (78 itens), Granny Nila (39) e Ditto (14), iguais nas duas
referencias de servidor preservadas e presentes no catalogo do cliente.
Goldie oferece tambem o acesso a forja existente. O cabeleireiro Cartel foi
ligado ao salao; Peter, Margaret, Beldi, Daniel e Mysterious Granny nao abrem
mais o salao indevidamente.

Ditto prepara quatro receitas originais: 1 Bottle (1779), 10 unidades do material
3129/3130/3131/3132 e 50 ouro produzem respectivamente o item 3133/3134/3135/3136.
A operacao valida todos os requisitos antes de alterar inventario/ouro e
preserva atributos dos itens nao consumidos. Gina restaura HP/MP/SP por 200 ouro;
a isencao original exige nivel menor que 6 e registro da missao 500 concluido.
Essa missao ainda nao foi importada; nao se concede a isencao apenas pelo nivel.
Nao se cobra por uma cura quando todos os recursos ja estao completos.

Compras usam o preco/limite de pilha do iteminfo. Vendas usam metade do preco
base, como no cliente original, e protegem itens equipados/refinados/com sockets.
Inventario cheio, quantidade invalida e ouro insuficiente nao geram cobranca
ou consumo parcial. A janela de NPC lista as missoes configuradas; aceitar e
entregar requer contato com o NPC correto (oferta expira em 60 segundos).

`Tools/PKO/Import Verified Argent NPC Services` compara novamente as fontes e
preserva uma copia da cena em `Library/TOPAutosave/NpcServices` antes de salvar.
Recusa fontes divergentes, IDs ausentes ou sobrescrita de configuracoes customizadas.
O resultado fica em `Tools/npc-services-import-results.txt`; a auditoria real dos
55 NPCs ativos e gerada em `Tools/npc-catalog-audit.txt` pelo teste isolado.

Jimberry ainda nao recebeu estoque: as referencias divergem (67/69 itens).
Banco, navegacao de navios, teleportes sem destino, cura parcial e regras de quests completas
nao foram inventados. As missoes originais incluem flags, multiplos objetivos e
recompensas condicionais que o modelo simplificado atual ainda nao representa.
O teste isolado acima cobre tambem NPCs, compras/vendas, receitas, cura e limites
de acesso; usa personagens sinteticos sem gravacoes em contas/banco reais.
Os 125 checks passaram. A fase social/NPC desliga os spawners e os inimigos do
fixture; a fase NPC pausa a regeneracao para medir cura/cobranca sem interferencia.

### Barcos e PK: implementacao local e limites

Sinbad (87) oferece os barcos originais 1/2/3/6 do berth Garner 1, com selecao
de casco, motor, proa, canhao e componente a partir de shipinfo/shipiteminfo.
O Guppy padrao custa 9990 ouro. Nivel/classe, partes compativeis, nome ASCII
de 2 a 16 caracteres, saldo e limite de tres barcos sao validados no servidor.
A compra salva frota e ouro na mesma transacao revisionada: o resultado so
fica visivel apos confirmacao; falhas definitivas nao cobram e respostas
incertas exigem recarga autoritativa. A API precisa anunciar BoatOwnershipVersion 1;
servidores/API antigos nao permitem construir nem podem confirmar silenciosamente
um save de barco ignorado.

As duas APIs receberam a migration aditiva `0006_boat_ownership.sql`.
Ela nao foi aplicada em producao nesta etapa. Saves antigos sem Boats preservam
a frota; uma lista vazia explicita remove a frota. A construcao e manutencao
existentes agora incluem lancamento/atracacao, navegacao server-authoritative e
combustivel/HP persistidos pelo mesmo caminho revisionado. O deed 3988 ainda nao
esta implementado.

Shirley (88) recebeu reparo, abastecimento e resgate de barcos no berth Argent 1.
Os comandos exigem a interacao autoritativa com seu modelo original, personagem
vivo, ausencia de duelo/trade e operacoes atomicas pendentes, frota propria e
saldo suficiente. Reparo custa `floor(HP faltante * .05) + nivelPersonagem * 20`;
abastecimento custa `combustivel faltante + nivelPersonagem * 20`. Os dois
servicos sao gratuitos para nivel <=10 e nao cobram quando nao ha necessidade.
O HP maximo neutro usa `Boat_plus_Mxhp` original, incluindo a mudanca no nivel
60: o Guppy nivel 1 possui 1919 HP efetivos (2280 e o valor base das pecas,
antes do modificador de nivel). Bonus de skills navais ainda nao foram portados.
Resgate custa 1000 ouro e apenas remove o estado afundado, sem dar HP/combustivel
gratuitos. Barcos afundados precisam ser resgatados antes de manutencao.

Ouro e barco mudam somente depois da confirmacao revisionada da API. Reservas
de inventario bloqueiam operacoes concorrentes; retry usa o mesmo corpo/ID,
rejeicao definitiva nao cobra, e respostas incertas obrigam reload autoritativo.
Snapshots da frota copiam cada registro mutavel para nao alterar o barco vivo
antes da confirmacao. As APIs anunciam/confirmam `BoatServicesVersion = 1`;
manutencao fica bloqueada em APIs antigas, mesmo se suportarem construcao.
O campo `IsSunk` usa o JSON naval existente, sem outra migration. Shirley (88)
lanca o barco proprio e nao afundado no berth 1 para o spawn original de sea
(2260,2829), direcao 177; o ancoradouro verificado em Garner (2231,2827) permite
atracar e retorna o personagem a terra. O estado `BerthId=0` identifica barco no
mar e e restaurado ao reconectar. Movimento usa velocidade de `shipiteminfo`,
so aceita destinos fora do NavMesh terrestre e fora das celulas bloqueadas da
grade original; o servidor replica o transform e as alteracoes de frota aos demais.
A cada 5 s, BSREC reduz SP/fuel; com fuel em zero, o HP perde 2,5% do maximo.
Ao afundar, HP chega a zero, `IsSunk` e salvo e o personagem retorna ao ultimo
berth de Argent para resgate. Os saves de lancamento, atracacao, fuel e afundamento
usam a transacao de personagem/versao ja existente, sem nova migration.

### Carga naval e frete

Os agentes originais Huradar/Moken/Soraris (NPCDefine 119/121/140) usam as linhas
de cliente 120/122/141 para mostrar a janela de carga junto ao dialogo NPC. O comando KCP exige interacao atual, alcance/mapa,
personagem vivo, barco proprio nao afundado, capacidade livre e ausencia de trade,
duelo, save concorrente ou operacao de inventario pendente. Dez unidades de madeira
4543 geram uma pilha 4547; os minerios 4544/4545/4546 usam o mesmo PackBag de 10 para
4548/4549/4550. Cada pilha ocupa uma unidade da capacidade original `shipinfo` do barco.

A carga e o inventario consumido sao escritos juntos na lista naval `boats_json` existente.
A entrega remove a pilha e credita seu valor `iteminfo` (4547/4548: 200, 4549: 300,
4550: 600 ouro por unidade) apenas depois da confirmacao revisionada da API. As APIs
MariaDB e Cloudflare validam os manifests iguais; nao foi necessaria migration nova.
Falha definitiva nao consome recursos nem paga recompensa; resultado incerto exige
recarga autoritativa. `BerthId=0` continua permitido para barcos no mar.

A populacao de Argent conserva os modelos originais e nao cria geometria substituta.
Na tabela cliente deste checkout, as linhas 120/122/141 aparecem como Freight - Huradar
(Abandon Mine Haven), Freight - Moken (Rockery Haven) e Freight - Soraris (Belmont
Plains); nenhum desses mapas/objetos esta na cena Garner carregada. O roteamento de frete
usa a diferenca de um ID entre NPCDefine e npclist; a entrega nesses portos fica indisponivel
ate a troca de mapas e os modelos originais dessas areas serem importados.
O arquivo `E:\NEW SV\File 2.rar` presente no ambiente nao pode ser listado pelo 7-Zip,
entao os dialogs/recompensas Lua/C++ especificos desses NPCs nao puderam ser lidos
para comparar ofertas diferentes. O pagamento implementado usa os precos originais
de `iteminfo` das pilhas, sem inventar multiplicadores por porto.
Validação anterior de barco/carga: 270 checks Unity e sete testes de API passaram;
o cliente/servidor de staging `20261009-180756` compilou sem erros e o teste KCP
isolado UDP17894 completou duas sessoes, 11 pongs cada. O servidor publicado
PID30220/UDP7777 ficou ativo.
### Persistencia das missoes nas APIs

A API Cloudflare agora tem as mesmas cinco rotas de progresso da API MariaDB:
listar, aceitar, abandonar, atualizar progresso e concluir. Ambas verificam
posse do personagem e entradas numericas; pedidos repetidos nao duplicam missoes
nem concluem duas vezes. Progresso atrasado nao reduz a contagem, e uma missao
concluida nao pode ser apagada pelo comando de abandono.

A migracao aditiva `Cloudflare/migrations/0003_quest_progress.sql` cria somente
a tabela ausente. Em 9 de outubro de 2026, com autorizacao para atualizar os
jogadores, foi aplicado esse SQL no D1 depois de um backup privado e publicadas
as Functions em `gamekg.pages.dev`. A API respondeu ao health check e a rota de
quests recusou acesso sem autenticacao com HTTP 401. Isso nao importa os roteiros
originais nem corrige a atomicidade entre recompensas/inventario e conclusao da
missao; essa integracao ainda precisa ser implementada antes da ativacao ampla.

`node --test Tools/tests/quest-api.test.cjs Tools/tests/new-character-api.test.cjs`
testa as duas APIs com SQLite em memoria, incluindo a autenticacao Cloudflare,
concorrencia, posse, validacao, progresso e preservacao dos dados ao reaplicar
a migracao. Com `TOP_TEST_MARIADB=1`, tambem testa o SQL real no MariaDB local,
usando apenas tabelas temporarias da conexao e a precedencia `.env.local`/`.env`
da API. Foram executados os cinco testes, incluindo MariaDB, sem falhas.
O resultado fica em `Tools/quest-api-results.txt`.

No desenvolvimento posterior a essa publicacao, refresh recusado passou a
preservar o estado local, e uma resposta atrasada nao sobrescreve progresso
alterado durante o pedido. Aceitar, abandonar e entregar a mesma missao
suprimem pedidos simultaneos; falhas de abandono/conclusao exibem aviso.
O fixture `QuestPersistenceSmokeTest` usa HTTP real em localhost com respostas
atrasadas e recusadas, sem tocar a API publicada. Essas mudancas posteriores
nao estao na build itch descrita abaixo. A atomicidade de itens/recompensas
na entrega de coleta permanece pendente.
Os 135 checks do teste social/NPC/quest passaram. Os comandos sinteticos agora
resolvem o hash Mirror pelo tipo do componente, evitando confundir metodos
homonimos de `PlayerController` e `PlayerMovement`. O cancelamento de aproximacao
confere o NPC pendente e o destino realmente recebido, nao apenas se o
personagem parou.

### Entrega de coleta: reserva de inventario (desenvolvimento)

A entrega agora prepara consumo e recompensa em um snapshot, sem remover
materiais antes da resposta da API. Durante a confirmacao, bolsa, equipamento,
consumiveis, forja, lojas/receitas, trade e correio nao podem alterar o inventario
reservado. Uma operacao de correio ja em andamento tambem impede abrir a reserva.
Uma resposta recusada libera a reserva e deixa os itens originais intactos.

Consumo entre stacks e espaco para recompensa sao verificados antes de chamar
a API, incluindo o slot liberado ao consumir completamente um material.
Itens equipados, bloqueados, refinados ou com sockets sao protegidos. A
confirmacao aplica o snapshot uma unica vez; instancias restantes preservam
ID, durabilidade, refino, gemas e bloqueio/proprietario. O hook de inventario no
host nao reconstrui os itens autoritativos a partir do payload reduzido do cliente.

Objetivos e recompensas podem usar IDs de itens exatos; definicoes antigas
continuam aceitando nomes. O diario mostra o nome do item pelo ID e a recompensa
de item, sem sobrepor os botoes. Nao foram substituidas as definicoes inferidas
pelas missoes originais neste passo.

Os 146 checks da reserva de gameplay passaram, incluindo recusa HTTP, inventario cheio,
IDs/quantidades invalidos, materiais protegidos, operacoes concorrentes e
confirmacao repetida. Isso e protecao em runtime: conclusao da quest e save do
personagem continuam sendo pedidos separados. Crash, desconexao durante entrega
ou perda da resposta depois de o banco confirmar ainda exigem uma transacao
persistente/idempotente com reconciliacao. Nao considerar a atomicidade duravel
concluida nem ativar em massa novas missoes de coleta antes desse passo.
Naquela etapa, essas alteracoes continuavam somente no desenvolvimento; itch e servidor publicado
permanecem na revisao `2026.10.09-gameplay-npcs-social`.

### Entrega atomica e saves versionados (desenvolvimento)

O desenvolvimento posterior substitui a entrega em dois pedidos por um unico
`PUT /api/game/characters/:id`, com `OperationId`, `QuestId` e
`Character.SaveRevision`. O mesmo save persiste inventario, skills, ouro, XP,
niveis/pontos e conclusao da missao, em uma transacao MariaDB ou um batch D1.
Um recibo persistente guarda hash do pedido e revisao: repeticao com o mesmo
conteudo retorna a confirmacao anterior, sem reaplicar itens/recompensas;
conteudo diferente com o mesmo ID e recusado. Saves atrasados sao rejeitados.
No D1, o trigger de recibos verifica revisao e missao ativa dentro do batch.

O Unity serializa saves e entregas por personagem, envia um corpo imutavel nas
tentativas e nao faz autosave/save de desconexao de um inventario reservado.
Se a confirmacao continuar incerta apos tres tentativas, bloqueia novos saves e
pede reconexao para recarregar os dados autoritativos; nao tenta desfazer uma
transacao que pode ja ter sido confirmada. XP de recompensa utiliza a mesma
regra de level-up do personagem. Uma entrega confirmada depois de destruir o
objeto do jogador continua recuperavel no proximo carregamento.

Migracoes novas: `Cloudflare/migrations/0004_character_save_transactions.sql`
e `API/migrations/0004_character_save_transactions.sql`. A API MariaDB aplica
seu schema aditivo ao iniciar. No D1, a migracao precisa ser aplicada
explicitamente antes de publicar as Functions novas. **Nao publicar somente
a API ou iniciar o servidor Unity de desenvolvimento contra a API antiga**:
o contrato de save mudou; coordenar migracao, API, Dedicated Server e cliente.
Essas migracoes ainda nao foram aplicadas em producao.

`node --test Tools/tests/character-transaction.test.cjs Tools/tests/quest-api.test.cjs Tools/tests/new-character-api.test.cjs`
valida transacao, rollback de inventario invalido, retry/replay, conflito de
conteudo, autosave antigo e recarregamento do personagem. Com
`TOP_TEST_MARIADB=1`, tambem usa SQL MariaDB real em tabelas temporarias,
sem alterar personagens reais. O fixture Unity testa HTTP real com resposta
perdida, repeticao do mesmo corpo e confirmacao unica da revisao.
Esses testes nao equivalem a um teste de crash do processo dedicado em producao.
Ainda faltam roteiros originais, objetivos mistos e flags; correio e outros
sistemas economicos nao foram convertidos para esta transacao neste passo.
O banco pessoal usa
32 espacos originais, sem armazenar ouro nem cobrar taxa; depositos/retiradas de itens,
incluindo stacks e atributos, sao validados pelo servidor e persistidos na mesma transacao.
As APIs MariaDB e D1 preservam o banco quando servidores antigos omitem a capacidade;
aplique as migracoes `0009_character_bank_storage.sql` antes de habilita-lo.
Ouro/XP fora do limite inteiro exato de JSON/JavaScript (9007199254740991)
sao recusados explicitamente, em vez de arredondados silenciosamente.

Missao de coleta agora pode declarar `Objective.CollectionItems` com varios
IDs/quantidades exatos. Todos os materiais sao verificados e consumidos no mesmo
snapshot/transacao; faltar um deles nao consome os outros. IDs repetidos ou
invalidos na definicao sao recusados. O diario cresce por linha de material,
sem sobrepor as recompensas ou botoes. `RequiredLevel`/`MaximumLevel` definem
uma faixa inclusiva para aceitar/oferecer; zero no maximo preserva quests antigas
sem teto. Uma missao ja aceita continua entregavel depois dessa faixa.
As definicoes/IDs antigos nao foram substituidos: o script original de
`Leaves Collection` usa ID persistente 721, dez itens 1573 e tres itens 1574,
niveis 5-6; ele nao equivale ao catalogo inferido atual. A semantica dos argumentos
de `AddExp` foi confirmada no codigo C++ original na etapa seguinte (abaixo).
Repeticoes/flags e migracao de progresso ainda precisam ser integradas antes
de substituir essa definicao.

Validacao desta etapa: 164 checks Unity de mundo/social/NPC/quests passaram,
incluindo perda de tres confirmacoes, bloqueio de save incerto e recarregamento
autoritativo, UUID estavel dos itens, coleta com varios materiais e limites
de nivel. Os sete testes de API passaram, incluindo SQLite em memoria e SQL
MariaDB real isolado. Resultados persistidos em `Tools/social-gameplay-results.txt`
e `Tools/quest-api-results.txt`; nenhuma conta real ou banco de producao foi
alterado por esses testes. O teste do limite de tres metros usa uma coordenada
X inteira no NPC sintetico para medir exatamente a borda, sem arredondamento
do deslocamento por ponto flutuante.

Foram geradas builds pareadas desta etapa em
`Build/GameProjectKG.gameplay-client-20261009-093843` (972 MB) e
`Build/GameProjectKG.gameplay-server-20261009-093843`, ambas com zero erros.
O servidor novo foi iniciado apenas em UDP 17894 e passou duas sessoes KCP
locais de 12 segundos, com 11 pongs em cada uma, sem desconexao ou spawn sem
autenticacao. Somente o processo de staging foi encerrado; o processo publicado
continuou ativo. Esse probe verifica transporte, nao login JWT ou persistencia
de conta real. Resultados em `Tools/gameplay-staging-build-results.txt` e
`Tools/gameplay-staging-runtime-results.txt`. As novas builds nao foram
publicadas, e a migracao de recibos nao foi aplicada ao D1 de producao.

### Regras originais de XP e recuperacao de progresso

Na continuacao de desenvolvimento, o diario de coleta mostra a quantidade
atual utilizavel/necessaria para cada material, inclusive objetivos com varios
itens. A contagem usa a mesma regra da entrega: itens equipados, bloqueados,
refinados ou com gemas nao sao contabilizados. Essa melhoria visual foi
publicada na build 2093377, substituindo a 2092791.

### Objetivos mistos de quests

`QuestDef.AdditionalObjectives` adiciona objetivos independentes ao objetivo
principal, ate 16 no total. Mortes, conversas e coletas podem ser combinadas;
cada morte/conversa usa seu proprio contador, e a entrega exige todos os
objetivos. Coletas consultam o inventario atual, nao um contador historico.
Materiais repetidos entre objetivos sao somados antes da transacao atomica;
o diario aloca as quantidades entre linhas sem contar a mesma pilha duas vezes.
Objetivos nulos, tipos invalidos, quantidade base invalida ou mais de 16
objetivos nao permitem oferta/aceite. Nenhum ID ou roteiro inferido
ja persistido foi substituido por essa infraestrutura.

A migracao aditiva `0005_quest_objectives.sql`, disponivel para D1 e MariaDB,
mantem o contador legado em `quest_progress.progress` e armazena os objetivos
adicionais por indice. As APIs validam indices 0-15, preservam o progresso
monotonico e recusam atualizacoes de quests inexistentes/concluidas.
Abandonar remove os contadores adicionais por FK em cascata, para um novo
aceite iniciar zerado. O setup Node aplica a migracao ao iniciar; o D1 requer
backup e migracao antes de publicar as novas Functions. Essa migracao foi
aplicada em producao na publicacao autorizada abaixo.

Refresh, fila de recuperacao HTTP e sincronizacao Mirror preservam cada
contador independente. Entrega/abandono aguardam qualquer progresso em voo
da mesma missao. A fila continua em memoria, sem journal duravel ou IDs de
episodio; repetir quests e os flags/roteiros intermediarios completos do
original ainda nao foram ativados.

O inventario sincronizado inclui o bloqueio do item para o cliente mostrar a
mesma quantidade utilizavel que o servidor. O novo leitor preserva mensagens
antigas de 8/9 campos, mas o novo payload de 10 campos exige cliente atualizado:
publique cliente e servidor pareados, nao misture essa build com clientes
antigos. A build anterior foi preservada antes da publicacao pareada.

Validacao: 193 checks Unity passaram (zero falhas), incluindo objetivos
mistos, refresh HTTP, payload do cliente remoto, entrega atomica unica,
materiais repetidos, bloqueios e compatibilidade de leitura dos formatos
legados. Os sete testes de API passaram, incluindo SQLite/D1 isolado e
upserts MariaDB reais em tabelas temporarias. Resultados em
`Tools/social-gameplay-results.txt` e `Tools/quest-api-results.txt`.

Builds pareadas de staging desta revisao:
`Build/GameProjectKG.gameplay-client-20261009-114344` (972 MB, zero erros) e
`Build/GameProjectKG.gameplay-server-20261009-114344` (zero erros).
A build dedicada foi testada apenas em UDP 17894: duas sessoes KCP de
12 segundos, 11 pongs cada, sem desconexao ou spawn sem autenticacao.
Somente o processo de staging foi encerrado; o listener publicado em 7777
permaneceu intacto. O probe de transporte nao testa login JWT ou saves
de conta real. Resultados em `Tools/gameplay-staging-build-results.txt` e
`Tools/gameplay-staging-runtime-results.txt`.

Publicacao autorizada em 09/10/2026: cliente itch Windows 2093377, versao
`2026.10.09-quests-mixed-objectives`, com processamento concluido. API
`7cfe232f.gamekg.pages.dev` e servidor dedicado foram atualizados juntos.
A migracao 0005 foi aplicada apos backup SQL privado; a tabela adicional e
as chaves estrangeiras foram verificadas sem violacoes. Todos os 264 arquivos
do servidor ativado foram conferidos por hash contra staging.
O listener UDP 7777 respondeu a duas sessoes pelo endpoint playit publico,
11 pongs em 12 segundos cada, sem desconexao ou spawn sem autenticacao.
Rotas de quests/personagem sem credenciais retornaram HTTP 401.
Os jogadores precisam atualizar o cliente antes de reconectar.
O codigo estava previamente commitado/sincronizado na revisao `15a0db596`;
nenhum novo commit/push foi feito por esta publicacao. Relatorio em
`Tools/gameplay-publication-results.txt`.

### Catalogo Lua nativo, flags e episodios

`Tools/Import-OriginalQuests.cjs` le as definicoes declarativas sem executar
Lua. Por padrao extrai somente scripts/tabelas de missoes do arquivo original
`E:\NEW SV\File 2.rar` para o cache local ignorado
`Tools/OriginalQuestSource`. Tambem aceita um diretorio MisScript como argumento.
O catalogo reproduzivel fica em `Assets/Resources/OriginalQuests.json`;
`Tools/original-quest-coverage.json` lista IDs, fontes, hashes e o motivo de
cada exclusao. No Editor, **Tools > PKO > Import Original Lua Quests**
regera o catalogo com Node.js e reimporta o JSON. Em batch, use
`-batchmode -executeMethod OriginalQuestImporter.RunBatch` (sem `-quit`).
O importer tambem pode ser reexecutado com Node, sem abrir o Editor.
Nao depende de Lua/arquivos externos no servidor compilado.
O runtime carrega esse JSON via `Resources.Load`; o batch social verifica o
recurso empacotado, o registro de todas as definicoes e condicoes desconhecidas
que devem falhar fechadas, alem das cadeias, triggers e recompensas.

Cobertura desta revisao: **962/1508 definicoes**, incluindo **952 exatas do
NEW SV** e **10 definicoes de mudanca de classe da referencia original**.
No arquivo NEW SV essas dez exigem itens customizados 15072/15073 ausentes
nas tabelas de itens fornecidas. Em vez de inventar itens/recompensas, foram
importadas as definicoes completas da referencia
`E:\ToP Server,client,Db,tools\ToP Server,client,Db,tools\GameServer`;
as substituicoes e os hashes dessa fonte sao explicitados em
`Reconciliations`. A diferenca de recompensas em relacao ao NEW SV nao e
silenciosa. **546 definicoes permanecem desativadas**: scripts sem binding
original de NPC, geradores aleatorios, funcoes especiais como
`AddExpAndType`, creditos/honra/guilda/navegacao/eventos, controles Lua
dinamicos, itens ausentes e triggers nao resolvidos. O relatorio enumera todas,
com motivos sobrepostos quando necessario; nenhuma condicao desconhecida e
tratada como verdadeira.

**137 definicoes** tem pelo menos um dos 55 NPCs ativos atuais; as outras
825 importadas nao recebem um NPC substituto inventado. Bindings usam o nome
exato do NPC e somente chamadas `AddNpcMission` nao comentadas. Senna e
varios NPCs de ilhas nao existem no mundo atual: partes de suas cadeias podem
ficar inacessiveis. O teste de Senna usa um NPC sintetico isolado, sem adicionar
um NPC persistente a cena. Peter/William executam etapas reais da promocao de
Swordsman (12 Piglets, carta, coleta de 3 itens e certificado); os NPCs e os
monstros ausentes continuam sendo uma limitacao de cobertura do mundo.
O resultado por NPC fica em `Tools/original-quest-world-coverage.txt`.

Os IDs de exibicao originais usam offset **1000000**; por exemplo, a definicao
702 torna-se 1000702, mas sua missao/registro nativo continua 701. Assim, as
quests inferidas e seus saves anteriores nao sao sobrescritos. Registros
`HasRecord/SetRecord/ClearRecord` sao independentes dos flags por missao;
`ClearMission` remove flags/triggers, mas preserva registros. Condicoes
conjuntas de nivel (comparadores exatos), classe, raca, itens, dinheiro,
missoes anteriores, registros e flags sao revalidadas pelo servidor. Episodios
`COMPLETE_SHOW` podem consumir uma carta/setar um flag em outro NPC sem
encerrar a missao-pai. Contadores de mortes/coletas usam os IDs numericos e as
faixas de flags originais, com clamp no total. A identidade dos monstros usa
nome exato mais nivel nativo; variantes ainda ambiguas exigem o ID explicito
no EnemyStats, em vez de contar todas por substring. Coletas verificam tambem a
posse atual quando o Lua exige `HasItem`. Repeticoes seguem os proprios
`NoMission/NoRecord/ClearRecord`, sem aplicar o bloqueio legado de IDs
concluidos. XP conserva minimo inclusivo/maximo exclusivo; ouro, multiplos
itens e classe sao aplicados conforme as acoes, com os resguardos existentes
para itens equipados/bloqueados/refinados. Nao foram inventados modifiers
globais de XP, timers, eventos ou teletransportes.

Aceite, episodios, abandono, flags/registros, itens e recompensas usam o
mesmo save atomico de personagem com receipt e SaveRevision; a fila de
progresso coalesce os flags e aguarda confirmacao antes de uma transicao.
Callbacks repetidos reutilizam o mesmo OperationId/payload. O estado e
sincronizado apenas ao dono por Mirror; comandos de cliente nao podem setar
flags/registros ou completar uma etapa remotamente. Como na fila legada,
um evento perdido antes de qualquer confirmacao HTTP nao e um journal duravel.

A migracao aditiva **0010_original_quest_state.sql**, em **API/migrations** e
**Cloudflare/migrations**, adiciona somente `characters.quest_state_json`.
O setup MariaDB aplica a migracao; D1 precisa de backup e aplicacao explicita
antes de uma publicacao futura. `QuestStateVersion=1` no load/save e
`questStateVersion=1` na confirmacao impedem recompensas somente em memoria
contra uma API antiga. Saves sem a capability preservam flags/registros e a
classe. As duas APIs mantem o mesmo contrato; nenhuma migracao/producao foi
aplicada ou publicada nesta tarefa.

Validacao: **359 PASS/0 FAIL** no batch social Unity (baseline 321, mais 38
checks nativos), incluindo KCP real, cartas multi-NPC, pre-requisitos, flags,
registros, coleta/mortes, retries/rollback, serializacao remota e mudanca de
classe com multiplas recompensas. **10/10 testes Node** passaram com
`TOP_TEST_MARIADB=1`, SQLite/D1 isolado e tabelas MariaDB temporarias.
Resultados em `Tools/social-gameplay-results.txt` e
`Tools/original-quests-node-results.txt`. Builds/transportes de staging
sao validados abaixo; nenhuma conta real foi utilizada, e nao houve
commit/push, publicacao, encerramento do servidor publico/tunel/cliente itch.

### Semantica nativa de XP e registros

Foi lida a implementacao nativa `lua_AddExp` em
`E:\PrivateTop\meu_servidor\sources\Server\GameServer\src\CharScript.cpp`.
Os dois argumentos sao minimo inclusivo e maximo exclusivo: `AddExp(40,70)`
concede 40-69 XP; se minimo >= maximo, concede o minimo fixo. O campo
`RewardExpMaximumExclusive` implementa essa distribuicao base; zero preserva
as recompensas fixas antigas. A recompensa e sorteada uma unica vez por entrega,
antes de serializar o save, e mantida nas tentativas do mesmo pedido.
O diario mostra o intervalo realmente possivel. Modificadores globais de
evento/XP do Lua (`GetExpState`) nao foram importados nesta etapa.

`RequiredCompletedQuests`, `ExcludedActiveQuests` e `ExcludedCompletedQuests`
permitem varios pre-requisitos e exclusoes, combinados com `PrerequisiteId`.
Oferta e aceite usam as mesmas condicoes; aceitar missoes e serializado para
nao contornar exclusoes durante um pedido HTTP pendente. Esses campos consultam
missoes concluidas/ativas atuais, nao substituem o sistema completo de flags
e etapas intermediarias do original.

Em ambas as referencias `NpcScript01.lua`, `AddNpcMission(733)` e
`AddNpcMission(738)` de Ditto estao comentadas. Elas nao foram ativadas ou
usadas para sobrescrever o catalogo inferido/persistido existente.

Progresso de mortes/conversas agora tem fila por missao, coalescendo novas
contagens enquanto um save esta em andamento. Falhas sao repetidas ate tres
vezes; a contagem nao confirmada permanece local e gera aviso explicito.
Atualizar o diario primeiro tenta confirmar esse progresso. Se falhar,
nao busca/aplica um snapshot antigo da API. Entrega/abandono da mesma missao
aguardam o pedido de progresso pendente. Atualizacoes de quest no host nao
reconstroem o estado autoritativo do servidor a partir do payload do cliente.

A fila de progresso ainda e em memoria: se o processo terminar antes de a API
confirmar, a contagem pendente pode se perder. Nao confundir essa recuperacao
de falhas HTTP com um journal duravel de eventos ou com a transacao atomica
de entrega. Repeticoes, flags originais e roteiros completos continuam pendentes.

Validacao adicional: 177 checks Unity passaram (zero falhas), incluindo
10.000 sorteios deterministas com as bordas 40/69, recompensa aleatoria mantida
apos resposta perdida, pre-requisitos/exclusoes, corrida de aceite,
coalescimento de mortes e refresh apos falha/recuperacao. Resultados em
`Tools/social-gameplay-results.txt`. Nenhuma conta real foi usada e nenhuma
API/build publicada foi substituida nesses testes.

As regras/progresso acima tambem foram compiladas em novas builds pareadas:
`Build/GameProjectKG.gameplay-client-20261009-102221` (972 MB) e
`Build/GameProjectKG.gameplay-server-20261009-102221`, ambas sem erros.
O servidor dessa revisao respondeu a duas sessoes locais UDP 17894 de
12 segundos, 11 pongs por sessao, sem desconexao/spawn sem autenticacao.
O processo de staging foi encerrado apos o probe; o servidor publicado
permaneceu ativo. Os resultados de build/runtime atuais estao em
`Tools/gameplay-staging-build-results.txt` e
`Tools/gameplay-staging-runtime-results.txt`. Nenhuma publicacao ou migracao
de producao foi executada nesta etapa; o probe nao testa uma conta real.

Publicacao posterior autorizada em 09/10/2026: a revisao
`bd566dbf148097803364fd9164387f155bffe38d` foi enviada ao GitHub main.
Apos backup SQL privado do D1, o servidor antigo foi parado antes da troca
do contrato de save; a migracao `0004_character_save_transactions.sql` e a
API correspondente foram publicadas (`0377dcd9.gamekg.pages.dev`).
As builds pareadas `20261009-102221` foram promovidas; cliente itch Windows
2092791, versao `2026.10.09-quests-atomic-progress`, com processamento concluido.
O servidor anterior foi preservado e o novo listener UDP 7777 foi verificado.
Duas sessoes pelo endpoint publico tiveram 11 pongs em 12 segundos cada,
sem desconexao ou entrada no mundo sem autenticacao. GET/PUT de personagem
sem credenciais retornaram HTTP 401. Esses probes nao validam login JWT
ou saves de uma conta real. Relatorio em `Tools/gameplay-publication-results.txt`.

`GameBuild.BuildGameplayStagingBatch` gera cliente Windows e Dedicated Server
em pastas novas com timestamp, sem substituir builds anteriores ou publicar.
O servidor gerado continua com configuracao local de staging (API local/7778);
o cliente tem o endpoint publicado e so deve ser usado depois de atualizar
o servidor compartilhado para a mesma revisao. Resultados em
`Tools/gameplay-staging-build-results.txt`.
Cliente e servidor foram gerados sem erros. A build dedicada foi iniciada em
UDP 17894 e respondeu a duas sessoes KCP de 12 segundos (11 pongs por sessao,
sem desconexao nem entrada de personagem sem autenticacao). Os dois destinos
do validador foram sobrescritos pela linha de comando para localhost: nenhuma
dessas sessoes testou o endpoint publico. Resultado em
`Tools/gameplay-staging-runtime-results.txt`. Somente o processo de staging
foi encerrado; o servidor compartilhado UDP 7777 permaneceu ativo.

### Publicacao gameplay de 9 de outubro de 2026

O cliente de staging foi publicado no canal `kg-online/kg-online:windows`,
build itch `2092000`, versao `2026.10.09-gameplay-npcs-social`. O servidor
correspondente foi copiado para `Build/GameProjectKG.editorserver`, preservando
a versao anterior em uma pasta `.previous.<data>`, e iniciado com API Cloudflare
e UDP 7777. O endpoint playit publico passou em duas sessoes KCP de 12 segundos,
11 pongs por sessao, sem desconexao. Isso verifica transporte, nao login real.
O relatorio fica em `Tools/gameplay-publication-results.txt`.

A tarefa `TOP-DedicatedServer` continuou desabilitada: a alteracao exigiu
administrador e a confirmacao UAC foi cancelada. O servidor foi iniciado
manualmente; a inicializacao automatica depois de reiniciar o Windows ainda
precisa ser habilitada com privilegios administrativos. Nao foram feitos
commit/push nem publicacao de fontes, credenciais ou backup.

### Ceu, sol e horario do servidor

**Tools > World > Ceu e Tempo** abre a previa de ambiente na GameScene.
Ative "Previa na GameScene" para ajustar horario, clima, direcao/intensidade/
diametro do sol, rotacao e exposicao do panorama. A previa restaura a iluminacao
original ao fechar, sair para Play ou salvar a cena; nao grava a hora no servidor.
"Salvar como padrao do servidor" grava somente os controles no asset
`Assets/Resources/WorldEnvironment.asset`, para a proxima inicializacao.

No jogo, administradores usam **F9** ou o botao **Ceu / Tempo**. "Aplicar para
todos" confirma inclusive os presets Amanhecer (06:00), Meio-dia (12:00),
Entardecer (18:00) e Noite (00:00); selecionar um preset apenas prepara o pedido.
O painel mostra o material predominante e o horario real em HH:mm.
O ajuste local de armas usa **Ctrl+F9**, para nao abrir duas janelas com F9.
F10 separa itens/equipamentos e personagem em abas; os paineis usam fundo opaco,
rolagem e limites de tela.

"Aplicar para todos" solicita uma alteracao ao servidor, que revalida a permissao pela API.
"Voltar ao horario real" remove o deslocamento manual do relogio. Alteracoes
durante o jogo duram nesta sessao do servidor; nao alteram o relogio do Windows.
Clientes normais apenas recebem o estado. Cliente e servidor precisam de builds
atualizadas com esse sistema; uma build dedicada anterior nao transmite o relogio.

O ciclo tem 24 horas reais no fuso local do servidor, com sol no horizonte as
06h/18h. E um ciclo artistico, nao um calculo astronomico de latitude/estacoes.
O servidor envia amostras a cada 2 segundos; os clientes interpolam o relogio
usando Mirror NetworkTime, sem depender do fuso do PC de cada jogador.
A GameScene recebe automaticamente o controlador, sem substituir a cena.
O Dedicated Server nao cria materiais, sol visual ou menu.
O controlador configura a camera principal da GameScene para desenhar skybox
(a configuracao anterior usava fundo solido) e restaura o modo anterior ao sair.

O shader faz crossfade dos oito panoramas FS002. Sunrise e Sunset aparecem
perto do horizonte; a noite se aprofunda conforme o sol desce abaixo dele.
O clima automatico usa blocos de 3 horas, com transicao de 10 minutos entre
variacoes deterministicas iguais para todos (60% dia, 20% sem sol, 10% chuva,
10% neve). Chuva/neve usam noite sem lua; a opcao Moonless tambem pode forca-la.
FS002_Day e o dia padrao e pode incluir um sol pintado na textura, alem do sol
movel: escolha Sunless para evitar esse segundo sol. Os materiais sao imagens,
nao animacoes; o movimento vem do sol e das transicoes do shader.
Rainy/Snowy mudam o panorama, sem particulas ou alteracao de gameplay.
Um skybox fica no infinito, portanto seu controle de posicao e por rotacao.

A lua movel acompanha o relogio do servidor (ciclo artistico de lua cheia,
sem fases astronomicas): nasce as 18h, culmina a 25 graus e se poe as 06h.
Essa trajetoria baixa permite ve-la na camera de jogo sem olhar ao zenite.
Seu disco artistico de 6 graus tem
superficie cinza com detalhes, sem halo solar e brilho independente do sol.
A luz direcional lunar azul-acinzentada tem intensidade maxima 0,12, contra
1,2 do sol padrao, com preenchimento ambiente suave para a noite ser legivel.
Ela desaparece suavemente no horizonte, de dia e sob
chuva/neve; "Noite sem lua" desativa disco e iluminacao lunar.
O panorama noturno sem lua serve de fundo para evitar uma segunda lua pintada.
O clima "Sem sol" nao desativa a lua: somente "Noite sem lua" e ceus de
chuva/neve a ocultam. A lua assume RenderSettings.sun durante a iluminacao
noturna para funcionar como luz principal URP, inclusive no Terrain/Lit.
F9 mostra sua altura no ceu; gire a camera para a direcao da lua para ve-la.
**Tamanho da lua** permite ajustar de 0,1 a 30 graus no F9 e na previa
**Tools > World > Ceu e Tempo**. O padrao e 6 graus. No F9 confirme com
**Aplicar para todos**; na previa a alteracao e local, e **Salvar como padrao**
persiste para a proxima inicializacao do servidor. O brilho nao muda com o tamanho.
Esse campo altera as mensagens Mirror: clientes e servidor devem ser atualizados
juntos antes de testar o multiplayer; nao conecte clientes novos a builds antigas.

A camera do jogador inicia com inclinacao de 22 graus e alvo elevado a 2,2m.
Botao direito permite inclinar de -35 a 75 graus; o percurso usa spherecast
contra Ground/Terrain e mantem pelo menos 0,5m de afastamento do chao.
O modelo visual do Enemy_Slime fica na origem local do inimigo, sem reutilizar
coordenadas de mapa como deslocamento do modelo. Os pontos de spawn permanecem.

**Tools > World > Configurar Fantasy Sky** instala as referencias de materiais
caso o asset esteja ausente. **Validar ciclo e materiais** na janela testa pesos,
continuidade, limites, renderizacao e restauracao de iluminacao; desligue a previa
antes de validar. Relatorio em `Tools/sky-validation-results.txt`.
O request explicito `Tools/validate-environment-network.request` executa o
smoke existente na porta temporaria 17892 e testa envio do relogio e rejeicao
de alteracao nao autenticada; nao autentica contas nem grava no banco.
O smoke tambem exige o terreno central `Garner_Argent`, verifica o raycast
na cidade e o deslocamento real do personagem mantendo a altura do chao.
As builds de cliente e servidor recusam a GameScene sem esse terreno ativo
e seu TerrainCollider. O ceu nao substitui nem recria terrenos.

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

### PK aberto e penalidades originais de morte (validação 2026-10-09)

Garner permanece seguro para PK letal. O servidor consulta os mapas PK originais
(`abandonedcity`/2/3, `darkswamp`, `DreamIsland`, `garner2`, `heilong`, `hell`/2/3/4/5,
`PKmap`, `prisonisland`, `puzzleworld`/2 e `teampk`) e seus atributos de terreno:
as duas posições precisam estar em terra/ponte (1/8), nunca em área segura (2).
Regras por tipo nativo protegem party no tipo 3, party e guild no tipo 4, guild no
tipo 2 e lado no tipo 5; associação desconhecida bloqueia o ataque onde exigida.
`garner`/cidades sem flag, mapas desconhecidos, células sem dados e mar não permitem
PK. Nas fontes verificadas, o nível não limita o ataque aberto. Cursor, menu,
clique/skill e servidor usam a mesma autorização; jogadores mortos, party/aliados,
trade e transações atômicas continuam protegidos. Duelo segue consentido e não letal.

`Dead_Punish` PvE foi portado para o servidor: nível <=10 e mapas `leiting2`,
`binglang2`, `shalan2`, `guildwar` e `guildwar2` não perdem nada; `garner2`
perde apenas SP; em `secretgarden`/`teampk`, personagens acima do nível 10 perdem
SP mas não EXP/durabilidade. Nos demais mapas, acima do nível 10, EXP perdida é
`min(floor(EXP_ate_proximo_nivel * 0.02), EXP_atual)`; SP vai a zero. Os itens
3846/3047/5609 (na ordem original) consomem uma unidade e evitam EXP/durabilidade,
mas não SP. Acima do nível 20, cada equipamento reparável equipado recebe 5% de
desgaste, limitado ao piso 49; slots e tipos seguem `Dead_Punish_ItemURE`. À noite
(18:00-06:00), set Pirate nível 70+ ou Death nível 75+ remove toda a penalidade.

Morte por jogador sempre zera SP. Nos mapas que chamam `MGPK_Dead_Punish_Exp`
(`puzzleworld`/2, `abandonedcity`/2/3, `darkswamp`, `hell`/2/3/4/5 e `heilong`),
a perda de EXP é `min(floor(nivel^2 * 20), floor(EXP_proximo_nivel * 0.02),
EXP_atual)`; no nível 80+ o valor é dividido por 50. O item 3846 consome uma
unidade e protege EXP/equipamento; sem ele, equipamentos reparáveis perdem 5% de
durabilidade, sem limite mínimo de nível. `teampk`, `PKmap`, `DreamIsland` e
`prisonisland` não sofrem EXP/desgaste PvP genérico (teampk ainda zera SP).
A entrada/cópia/arena original não foi portada: medalhas 3849, limites e
recompensas de honra permanecem pendentes porque seus atributos não são
persistidos. Não foram inventados pontos de honra nem regras de nível.

Não foi encontrado sistema geral de queda de itens no `Dead_Punish` original nem
existe ground-loot no projeto; nenhum drop foi inventado. `GetExp_PKP` não concede
recompensa de XP e não foram encontrados red-name/crime points; `PkPoints` não é
alterado por matar jogadores. A migration aditiva `0007_inventory_fusion_item_id.sql`
em `API/migrations` e `Cloudflare/migrations`, com ambas as APIs sincronizadas,
persiste IDs fundidos usados nas isenções noturnas. A migration não foi aplicada
em produção nem publicada; testes usaram bancos isolados/tabelas temporárias.

A morte salva snapshot de EXP, SP, inventário e durabilidade pelo endpoint
revisionado existente. Validação final: `TOPWorldEntrySmokeRunner.RunSocialBatch`
passou 280/280 checks (baseline anterior 270, mais 10 checks PK/death);
`TOP_TEST_MARIADB=1 node --test Tools\tests\*.test.cjs` passou 7/7, sem skips.
`GameBuild.BuildGameplayStagingBatch` gerou cliente/servidor sem erros em
`Build/GameProjectKG.gameplay-client-20261009-185741` e
`Build/GameProjectKG.gameplay-server-20261009-185741`. O staging UDP17894 passou
duas sessões KCP/Mirror de 12 s, 11 pongs cada, sem spawn prematuro/desconexão.
`Tools/multiplayer-validation-results.txt` foi restaurado byte a byte e apenas o
processo staging foi encerrado; PID30220/UDP7777 permaneceu ativo. Nenhum
commit/push/publicação foi feito. A validação de transporte não cobre JWT,
contas reais nem persistência remota. Se uma transação atômica já estiver ativa,
o autosave de morte é adiado para o ciclo seguinte.
### Arena teampk integrada (2026-10-09)

The original Arena Administrator (client NPC 53, server record 52, r_talk87,
Argent City 2210,2893) offers Medal of Valor creation and solo/party registration.
Creation requires level **>25**, 50000 gold, no existing item 3849, and a free
inventory slot. Original grade 97 initializes STR/STA/CON/AGI/DEX to **10**:
honor, wins, entries, kills, and deaths. The medal cannot be sold, dropped or
destroyed. Admission requires exactly one unequipped unit with honor in the
inclusive range **-300..30000**; existing medal holders have no additional
admission level requirement.

Registration consents to matching against another registration of the same
mode. Queues expire after 120 seconds; party leaders register at most 5 nearby,
living members without pending duel/trade/transaction activity. Queue matching
adapts the native challenge screens to the current UI; the old native challenge
list is not reproduced. The server has exactly **20 isolated copies**, with
synchronized instance ID, side and result. Mirror interest management filters
observers by instance ID, and combat/skills revalidate it. Room terrains are
128m apart outside Garner, preventing collisions/AoE between copies. PKOArena
uses the original 96x96 teampk.map and four original floor textures, retaining
native heights and movement blocks. Texture overlay masks and decorative
scenery have not yet been reconstructed.

Party PVP 1/2 uses native spawns **(44,21)/(44,66)**. Entry increments CON.
Kills increment attacker AGI/victim DEX and apply original honor changes:
attacker-minus-victim level difference strictly between -5 and 10 gives +1/-1;
>=10 gives 0; <-5 gives +2/-2; exactly -5 gives 0, preserving the Lua gap.
Eliminating one side resolves the match using the original base-2 honor formula,
initial participant counts and floored average levels, Lua floor for negative
divisions, and multiplier caps of 3. Wins increment STA. Admission bounds do
not truncate ongoing kill/result rewards: medals can leave outside those bounds.

The arena closes **11 seconds after the result** and returns all participants
alive to **Argent Bar (2207,2887)**. Generic 5-second respawns cannot revive arena
casualties. Inventory mutations, party changes, duels and trades are guarded
inside instances. Active-arena saves persist the return position in Garner,
not temporary instance coordinates. Honor/counters use the existing revisioned
save path; API/game.js and Cloudflare/functions/api/[[path]].js have the same
contract. Additive **0008_arena_medal_attributes.sql** exists in both backends;
it has not been applied to production or published. Database tests use isolated
databases/temporary tables. No git commit, push or publishing was performed.

Final-source RunSocialBatch: **303 PASS / 0 FAIL** (280 existing checks plus
23 arena checks), including real KCP NPC commands, medal creation/admission,
honor bounds, two live copies, observer/combat isolation, traversable native
spawns, terrain recreation after destruction, solo/party results, honor beyond
admission bounds, pending respawn cancellation and return after 11 seconds.
TOP_TEST_MARIADB=1 node --test Tools\tests\*.test.cjs: **7 PASS / 0 FAIL /
0 skipped**, including medal attribute round-trips and invalid-value rejection
in both backends.

Final staging build (no promotion): client
`Build/GameProjectKG.gameplay-client-20261009-202947` and dedicated server
`Build/GameProjectKG.gameplay-server-20261009-202947` both **Succeeded, 0 errors**.
UDP17894 staging transport passed two 12-second KCP/Mirror sessions: **11 pongs
each, 0 premature world spawns, no disconnects**. The original
`Tools/multiplayer-validation-results.txt` was restored byte-for-byte (SHA256
`FD0B155CB993F7B4D416CCE8FE9A629742B15DFA0FAC469D66D9F1ECEAA9AFC8`);
the arena-specific result is retained in `Tools/arena-multiplayer-validation-results.txt`.
Only task staging PID29456 was stopped. Public PID30220/UDP7777 stayed active;
no commit, push, production migration or publication was performed. Transport
validation does not authenticate real accounts/JWT or test remote persistence.

### Original guild / Goldie / fairy / stalls / skill core (local, not published)

- Guild creation now requires interacting with **Mas**, **100,000 gold** and
  **Stone of Oath (1780)**. Membership creation, stone consumption and payment
  share the character save receipt transaction. Invites require online consent
  (30 seconds); leave/kick/rank controls, name display and guild-only chat
  (`/g`) use the existing guild UI. Offline invitations/custom ranks/guild banks
  are not implemented. The formerly separate creation/debit endpoint is disabled.
- Goldie's forge now reserves inventory and confirms the entire item/material/gold
  snapshot before applying it. Existing forgeitem rates/failure levels remain.
  **Fusion** consumes Scroll 453 and, for refined/socketed gear, Catalyst 454,
  at equipment level x 1,000 gold; compatible full-durability apparel retains
  equipment stats through FusionItemId. **Gem combining** costs 5,000 gold,
  consumes equal gems and a type-47 scroll, and uses forge.lua's type-49/50 rates.
  Bonus fruits, per-GemVar maximum levels, socket insertion/upgrading and
  strengthening remain gaps; combining is conservatively capped at level 9.
- Fairy equip reuses the original type-59 Pet slot. Persistent item-keyed growth,
  stamina (raw original units, initial 5,000), five attributes, normal/great fruits and ration IDs follow
  functions.lua/variable.lua: 60-second growth ticks (slower after level 27),
  50 stamina spent, +1 growth, 6,480 growth cap, original success probability,
  normal level cap 42. The F9 Fairy/Barracas window feeds the equipped fairy.
  Marriage/possession, auto-feed, coins and fairy skill books are not implemented;
  depleted stamina disables added fairy attributes. Improved fruits are blocked.
- Original source has **GM/Item Mall mail** (CharTrade.cpp MailInfo), not the
  invented player attachment service. Player sending/legacy claiming are disabled
  in Unity and both backends; no unsafe attachment/gold claiming remains enabled.
  Original GM/Item Mall delivery is a remaining integration, not player mail.
- Player stalls require learned **Set Stall (241)**, living on land outside
  duel/arena. F9 sets item quantities/unit prices, shows nearby shops and buys;
  offers persist and inventory/movement/casting are reserved while open.
  Purchases lock both character save gates, then atomically update both inventories,
  both gold balances, offers and revisions with an idempotent purchase receipt.
  Seller must be online; offline shops, currency alternatives, original per-tier
  slot limits and fairy-attribute trading remain unsupported (fairy sales blocked).
- All skillinfo rows already imported generically; learning now checks original
  class maxima (including promoted/all-class rules), prerequisites, levels and
  points, persists transactionally and reloads learned skills. Damage/heal/SP
  arithmetic from target parameters, cooldowns, cone/friendly targeting are
  executable. Full Lua skilleff buffs/debuffs/passives/summons and weapon-specific
  discharge restrictions are **not** fully ported; generic availability is not a
  claim of complete original combat parity.

Migration **0011_gameplay_social_state.sql** exists additively in both migration
folders. GameplayStateVersion=1 is persisted with existing save revisions and
requires gameplayStateVersion=1 acknowledgement; older endpoints fail closed.
No production migrations, commit/push or publication are part of this change.

Gameplay operations additionally preflight authenticated `/game/capabilities`,
which verifies migration columns/tables before any item/gold mutation. An old
backend or missing 0011 schema is rejected before submitting a character save;
post-save acknowledgements remain mandatory.

`Tools/ImportOriginalSkillParameters.cjs` derives numeric per-level SP/cooldown
parameters from original `skilleffect.lua` and `skillinfo.txt` without executing
Lua gameplay. Coverage: **411 skill rows**, **130 class-specific rows**, **194
skilleff rows**, all **19 nonzero target arithmetic formulas**; **394 SP-cost**
and **388 cooldown** rows have safely imported numeric parameters. Unsupported
conditional Lua parameter functions retain the legacy fallback and remain a
combat-parity gap. Illusion Slash now costs 20 SP/5 seconds; Sacred Ray cooldown
varies by learned level rather than parsing its expression as a fixed integer.

Fusion accepts full unfused apparel templates with raw client durability 25,000
(display 500), while retaining support for the original server runtime 23,000
unfused marker; fused apparels cannot be reused as fresh left-side templates.

Player skill-point learning excludes unrestricted monster/native-internal rows
(`class=-1`) except the original universal Set Stall (241); importing a row does
not authorize a player to purchase NPC/monster abilities.

Canonical creation-NPC coverage limitation: Mas is native NPC 254, Icicle Royal
(1346,451) in Deep Blue, not Argent/Ascaron. The current populated Ascaron scene
does not contain that NPC. Creation remains deliberately proximity-gated to Mas
instead of relocating him or enabling arbitrary remote creation; deploying the
original Deep Blue/Icicle NPC scene is required for normal in-world creation.
Existing guilds can still use invite/leave/kick/rank/chat anywhere.

Guild names are rendered above spawned characters from the server-synchronized
PlayerGuild name, alongside the existing nearby-stall world labels.


Final validation of this source state: social batch **383 PASS / 0 FAIL**;
all Node tests with TOP_TEST_MARIADB=1 **15 PASS / 0 FAIL / 0 skipped**;
fresh staging client and dedicated server **2 successful builds / 0 errors**
(GameProjectKG.gameplay-client/server-20261010-002408); localhost UDP 17894
**2/2 sustained 12-second KCP probes**, 11 pongs each and zero premature world
spawns. These probes do not cover authenticated account/database end-to-end play.
The original multiplayer report was restored byte-for-byte; only staging PID
32704 was stopped. No commit, push, publication or production migration was run.

