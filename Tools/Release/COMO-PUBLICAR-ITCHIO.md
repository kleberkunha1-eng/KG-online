# Como publicar o jogo no itch.io (resolve o problema de antivirus de vez, e de graca)

## Por que itch.io?

O problema que estamos enfrentando ha varias sessoes - antivirus alterando/corrompendo arquivos
do jogo baixados pelo launcher proprio - acontece porque **o seu launcher e os arquivos do jogo
ainda nao tem reputacao nenhuma** no Windows/antivirus (certificado autoassinado, app novo,
ninguem conhece). Resolver isso sozinho custaria certificados pagos (US$150+/ano) e anos de
reputacao organica.

**O app itch.io ja resolveu isso pra voce**: ele e uma aplicacao estabelecida, reconhecida pela
grande maioria dos antivirus, que cuida de baixar, instalar e atualizar o jogo automaticamente
para o jogador. Voce so envia a build; o itch.io cuida do resto. E **gratuito**.

Isso nao substitui o launcher proprio obrigatoriamente - pode ficar como alternativa - mas e o
caminho mais rapido e sem custo pra garantir que o jogador consiga jogar sem o antivirus
atrapalhar.

## Passo a passo (uma vez so)

1. **Crie uma conta** em https://itch.io (gratis, só e-mail e senha).
2. **Crie a pagina do jogo**: no menu do seu perfil, "Upload new project". Preencha nome,
   genero (MMO/RPG), classificacao etaria, etc. Nao precisa de arquivos ainda - isso vem depois,
   via Butler (abaixo). Anote a URL da pagina: sera algo como `itch.io/SEU_USUARIO/SEU_JOGO` - o
   `SEU_USUARIO` e `SEU_JOGO` sao os valores que vamos usar no script.
3. Na pagina de edicao do projeto, em **"Distribution format"**, marque a opcao que permite
   builds por Butler (normalmente ja e o padrao quando voce nao faz upload manual de arquivo).
4. **Baixe e instale o app itch.io** (o cliente que os jogadores tambem vao usar):
   https://itch.io/app - use-o para conferir se a instalacao/atualizacao funciona do seu lado
   antes de divulgar aos jogadores.

## Publicar uma build (toda vez que tiver uma atualizacao)

### Opcao recomendada: um unico comando pros dois canais (itch.io + GitHub Releases/launcher)

```powershell
cd "Tools\Release"
.\Publish-Update.ps1 -Notes "Descricao da atualizacao"
```

Isso gera a build no Unity (se o Editor estiver aberto neste projeto), publica no GitHub Releases
(launcher proprio, com manifest/hashes) e no itch.io (Butler), nessa ordem. Use `-SkipUnityBuild`
se ja gerou a build manualmente, ou `-SkipGithub`/`-SkipItch` pra publicar em so um dos canais.
Veja `Publish-Update.ps1` (comentarios no topo) pra mais detalhes e parametros.

### Manual, so itch.io

1. Gere a build no Unity: **Tools > Build Game (Windows)** (igual ao processo atual, gera em
   `Build\GameProjectKG`).
2. Rode o script de publicacao (ele baixa o Butler sozinho na primeira vez):
   ```powershell
   cd "Tools\Release"
   .\Publish-Itch.ps1 -ItchUser "SEU_USUARIO" -ItchGame "SEU_JOGO"
   ```
3. Na primeira vez, ele vai pedir pra voce rodar `butler login` uma unica vez (abre o navegador,
   voce so autoriza a conta). Depois disso fica salvo nesta maquina e nao precisa repetir.
4. Pronto - a nova versao aparece na pagina do jogo e os jogadores com o app itch.io recebem a
   atualizacao sozinhos.

## E a API do jogo (contas, personagens, etc.)?

Nao muda nada: o jogo continua se conectando em `https://gamekg.pages.dev` (API/Cloudflare) do
mesmo jeito, independente de ter sido baixado pelo launcher proprio ou pelo itch.io. So muda
*como o executavel chega ate o jogador* - os dados de conta/personagem continuam no mesmo lugar.

## Status atual (ja feito)

- Conta criada: `kg-online` (https://kg-online.itch.io).
- Jogo publicado: https://kg-online.itch.io/kg-online (visibilidade: Public).
- Primeira build enviada via Butler (canal `windows`, versao 1).
- Comando usado para publicar (ja validado funcionando):
  ```powershell
  cd "Tools\Release"
  .\butler\butler.exe push "..\..\Build\GameProjectKG" "kg-online/kg-online:windows"
  ```
- Para novas atualizacoes, basta gerar a build no Unity de novo e rodar o mesmo comando -
  o Butler so envia a diferenca (patch), entao fica rapido depois da primeira vez.
- Verificar status de uma build: `.\butler\butler.exe status "kg-online/kg-online"`.

Nota: o dominio de download do Butler mudou de `broth.itch.ovh` para `broth.itch.zone` - o
script `Publish-Itch.ps1` ja foi atualizado com o endereco correto.
