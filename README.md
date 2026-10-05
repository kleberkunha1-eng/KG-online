# Tales of Pirates Unity

Projeto configurado para Unity 6000.4.4f1. Abra `Assets/Scenes/LoginScene.unity` e entre em Play. O login precisa da API Node em `API` (porta 3000) e do MariaDB configurado nela. Para iniciar a API: `cd API` e `node server.js`. Não execute uma segunda instância se a porta já estiver ocupada.

## Multiplayer

O cliente Unity usa Mirror/KCP e **nunca** inicia um host no computador do jogador. Após o login REST, ele lê o destino Mirror de `api.json` (ou dos argumentos `--game-server=HOST --game-server-port=PORT`) e conecta como cliente. Um `api.json` de produção deve conter:

```json
{
  "apiUrl": "https://sua-api.example",
  "gameServerHost": "game.example",
  "gameServerPort": 7777
}
```

Execute uma instância dedicada do mesmo build com `GameProjectKG.exe -batchmode -nographics --server --server-port=7777`. O servidor valida o JWT na API antes de liberar a seleção de personagem.

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

Última execução: 3 de outubro de 2026, 16 verificações aprovadas, sem erros capturados. Compilação no editor aprovada; nenhuma build standalone foi validada. Caminhada/corrida, dano, mana, XP, evolução, respawn, minimapa e integridade/altura do modelo foram exercitados.

No editor, use `TOP > Validate gameplay (local test)` para executar o teste isolado do mundo. Ele usa um personagem sintético e não grava contas/personagens no banco. Os resultados ficam em `Tools/smoke-results.txt`, erros em `Tools/smoke-errors.txt` e a captura em `Tools/gameplay-smoke.png`. Esse teste não substitui testes de login real nem de múltiplos clientes remotos.

`TOP > Configure playable world` reconstrói a configuração de cena/prefabs e o terreno. Salve cenas abertas antes de executá-lo. Essa ferramenta aplica posições e parâmetros definidos em `Assets/Editor/PlayableWorldSetup.cs`.

## Limpeza e origem

581 assets candidatos sem dependência das cenas/Resources, além de exemplos e cópias antigas, foram arquivados fora do projeto em `C:\Users\klebe\Tales of Pirates Unity Backups\20261003`. Sete dependências necessárias dos monstros foram restauradas durante a validação. Também foram removidas 12 dependências de pacotes sem uso identificado (lista em `Tools/removed-unused-packages.txt`). Os relatórios em `Tools` registram a seleção; o cliente original foi preservado.

A conversão de modelos usa `Tools/ConvertClientModel.cs` e estruturas do projeto [WeaponOwl/PKO-file-viewer](https://github.com/WeaponOwl/PKO-file-viewer). Fontes de referência do formato de mapa estão em `Tools/OriginalFormat`; o manifesto dos arquivos originais importados está em `Tools/import-manifest.json`.
