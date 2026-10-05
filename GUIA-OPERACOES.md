# Guia de Operações — Build, Servidor e Publicação

Guia único com todos os comandos do dia a dia: gerar build, rodar/gerenciar o servidor dedicado,
publicar no itch.io e enviar o código pro GitHub. Pense nisto como o "manual de procedimentos"
do projeto — não precisa decorar nada, é só seguir a receita da tarefa que você quer fazer.

> Veja também: [`Tools/Release/COMO-PUBLICAR-ITCHIO.md`](Tools/Release/COMO-PUBLICAR-ITCHIO.md)
> (contexto de por que usamos itch.io) e o [`README.md`](README.md) (visão geral do projeto).

---

## Índice

1. [Gerar a build do jogo](#1-gerar-a-build-do-jogo)
2. [Rodar o servidor dedicado](#2-rodar-o-servidor-dedicado)
3. [Publicar no itch.io](#3-publicar-no-itchio)
4. [Enviar código para o GitHub](#4-enviar-código-para-o-github)
5. [Fluxo completo (atualização ponta a ponta)](#5-fluxo-completo-atualização-ponta-a-ponta)
6. [Checar se está tudo no ar](#6-checar-se-está-tudo-no-ar)
7. [Problemas comuns](#7-problemas-comuns)

---

## 1. Gerar a build do jogo

### Opção A — Pelo Editor (mais simples, use sempre que possível)

1. Abra o projeto no Unity (versão `6000.4.4f1`).
2. Menu **Tools > Build Game (Windows)**.
3. Aguarde terminar (aparece no console: `Succeeded | XXX MB | 0 errors | tempo`).
4. A build fica em `Build\GameProjectKG\GameProjectKG.exe`.

### Opção B — Por linha de comando (sem abrir o Editor, útil se ele já estiver aberto noutro
projeto ou se você quiser automatizar)

> ⚠️ **Feche qualquer processo do próprio jogo rodando** (`GameProjectKG.exe`, incluindo o
> servidor dedicado) antes de buildar — o Unity não consegue sobrescrever arquivos travados por
> um processo em execução (erro típico: `UnauthorizedAccessException: Access to the path
> 'dstorage.dll' is denied`).

```powershell
# Pare o servidor dedicado primeiro, se estiver rodando (veja seção 2 - "Parar o servidor")

$unity = "C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe"
$project = "C:\Users\klebe\Tales of Pirates Unity"
$log = "$project\Tools\Release\logs\build.log"

Start-Process -FilePath $unity -ArgumentList `
  "-batchmode","-nographics","-quit","-projectPath","`"$project`"", `
  "-executeMethod","GameBuild.BuildMenu","-logFile","`"$log`"" `
  -Wait -PassThru | Select-Object ExitCode

# Confira o resultado:
Select-String -Path $log -Pattern "Succeeded|Failed|error CS"
```

Se aparecer `error CS...` no log, a build falhou por erro de compilação — corrija o código antes
de prosseguir. Se aparecer só `Succeeded | ... MB | 0 errors | ...`, deu certo.

### O que a build gera automaticamente

- `Build\GameProjectKG\GameProjectKG.exe` — o executável do jogo (cliente e servidor, é o mesmo
  binário; o que muda é o argumento `--server` na hora de rodar).
- `Build\GameProjectKG\api.json` — aponta para a API de produção (`https://gamekg.pages.dev`) e
  para o endereço do servidor dedicado, lido de `Tools\server-address.txt` (veja seção 2). Se
  esse arquivo não existir, a build sai com `gameServerHost` vazio e o jogo mostra um erro claro
  em vez de travar.

---

## 2. Rodar o servidor dedicado

O servidor roda **nesta própria máquina**, exposto para os jogadores através de um túnel
[playit.gg](https://playit.gg) (evita precisar abrir porta no roteador / expor seu IP).

### 2.1. Pré-requisitos (já configurados nesta máquina, só documentando)

- Serviço do Windows `playitd` instalado e com início automático (`Get-Service playitd` deve
  mostrar `Status: Running`, `StartType: Automatic`).
- Túnel UDP criado no painel [playit.gg/account](https://playit.gg/account) apontando para a
  porta local `7777`.
- Endereço público do túnel salvo em `Tools\server-address.txt` (1ª linha, formato
  `host:porta`, ex.: `pgsql-henderson.tun.ply.gg:22538`). **Esse arquivo não é versionado** — se
  trocar de máquina, precisa recriá-lo.
- Tarefa Agendada do Windows `TOP-DedicatedServer`: mantém o servidor rodando sozinho, inclusive
  após reiniciar o PC (gatilho "ao fazer logon"). Normalmente você **não precisa mexer nela
  manualmente** — ela já reinicia o jogo se ele cair.

### 2.2. Iniciar/gerenciar o servidor manualmente (sem depender da tarefa agendada)

```powershell
cd "Tools\Release"
.\Start-DedicatedServer.ps1
```

Isso roda o jogo com `--server --server-port=7777`, reinicia sozinho se cair, e grava logs em
`Tools\Release\logs\server.out.log` / `server.err.log`. Deixe essa janela aberta (ou rode via
tarefa agendada, que já faz isso em background).

### 2.3. Ver a tarefa agendada (forma recomendada no dia a dia)

```powershell
# Ver status
Get-ScheduledTask -TaskName "TOP-DedicatedServer" | Select-Object TaskName, State

# Forçar reinício agora (ex.: depois de gerar uma build nova)
Stop-ScheduledTask -TaskName "TOP-DedicatedServer" -ErrorAction SilentlyContinue
Start-ScheduledTask -TaskName "TOP-DedicatedServer"
```

### 2.4. Parar o servidor (necessário antes de gerar uma build nova)

```powershell
# 1. Pare a tarefa agendada (senão ela reinicia o processo sozinha)
Stop-ScheduledTask -TaskName "TOP-DedicatedServer" -ErrorAction SilentlyContinue
Disable-ScheduledTask -TaskName "TOP-DedicatedServer"   # evita reinício automático durante o build

# 2. Mate o processo do jogo (ache o PID e finalize)
Get-Process | Where-Object { $_.ProcessName -like "*GameProjectKG*" } | Select-Object Id
Stop-Process -Id <PID_ENCONTRADO> -Force
```

> Se der "Acesso negado" ao tentar `Stop-Process`, rode numa PowerShell **elevada** (Executar
> como administrador) ou use `Start-Process powershell -Verb RunAs -ArgumentList '...'`.

### 2.5. Religar depois de gerar a build nova

```powershell
Enable-ScheduledTask -TaskName "TOP-DedicatedServer"   # requer PowerShell elevada
Start-ScheduledTask -TaskName "TOP-DedicatedServer"

# Confirme que subiu:
Start-Sleep -Seconds 8
Get-Process | Where-Object { $_.ProcessName -like "*GameProjectKG*" } | Select-Object Id, StartTime
netstat -ano | Select-String ":7777"   # deve aparecer UDP 0.0.0.0:7777 com o PID novo
```

### 2.6. Ver os logs do servidor

```powershell
Get-Content "Tools\Release\logs\server.out.log" -Tail 50   # saída normal (Debug.Log)
Get-Content "Tools\Release\logs\server.err.log" -Tail 50   # erros/exceções
```

---

## 3. Publicar no itch.io

A build chega aos jogadores pelo app itch.io (eles recebem atualização automática). Usamos o
**Butler**, a ferramenta oficial de linha de comando do itch.io — o script já cuida de baixá-lo
na primeira vez.

### 3.1. Publicar uma build nova

```powershell
cd "Tools\Release"
.\Publish-Itch.ps1 -BuildDir "..\..\Build\GameProjectKG" -ItchUser "kg-online" -ItchGame "kg-online"
```

- `-ItchUser` / `-ItchGame`: já fixos neste projeto (`kg-online` / `kg-online`). Só mude se
  trocar de conta/jogo no itch.io.
- Na primeira vez numa máquina nova, ele pode pedir `.\butler\butler.exe login` (abre o
  navegador, você só autoriza a conta). Depois disso fica salvo e não pede de novo.
- O Butler só envia a **diferença** entre a build antiga e a nova (patch), então depois da
  primeira publicação fica rápido mesmo com a build tendo centenas de MB.

### 3.2. Conferir se a build foi processada

```powershell
cd "Tools\Release"
.\butler\butler.exe status "kg-online/kg-online:windows"
```

Deve aparecer uma linha tipo:
```
| windows | #19575829 | √ #2071840 (from #2071651) |       6 |
```
O `√` antes do número de build confirma que o itch.io já processou e os jogadores já podem
baixar.

---

## 4. Enviar código para o GitHub

O repositório remoto real (com permissão de push) é `kleberkunha1-eng/Tales-of-Pirates-Unity`
(não confundir com `guijove-afk/Tales-of-Pirates-Unity`, que é só leitura nesta máquina).

### 4.1. Commitar as mudanças

```powershell
git add <arquivos que você mudou>
git commit -m "tipo(escopo): descrição curta

Explicação mais detalhada do que mudou e por quê, se necessário.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### 4.2. Enviar (push) usando o token do GitHub CLI já autenticado nesta máquina

```powershell
$ghToken = & "C:\Program Files\GitHub CLI\gh.exe" auth token
$remoteUrl = "https://x-access-token:$ghToken@github.com/kleberkunha1-eng/Tales-of-Pirates-Unity.git"
git push $remoteUrl main
```

> Por que não só `git push origin main`? Porque o prompt interativo de credencial do Windows
> trava/falha neste ambiente — embutir o token direto na URL do push evita esse problema sem
> precisar salvar o token em lugar nenhum do repositório.

---

## 5. Fluxo completo (atualização ponta a ponta)

Receita padrão para quando você mexe no código do jogo e quer publicar a atualização para os
jogadores:

```powershell
cd "C:\Users\klebe\Tales of Pirates Unity"

# 1. Pare o servidor dedicado (libera os arquivos da build antiga)
Disable-ScheduledTask -TaskName "TOP-DedicatedServer"          # requer PowerShell elevada
Stop-ScheduledTask -TaskName "TOP-DedicatedServer" -ErrorAction SilentlyContinue
Get-Process | Where-Object { $_.ProcessName -like "*GameProjectKG*" } |
    ForEach-Object { Stop-Process -Id $_.Id -Force }

# 2. Gere a build nova (Opção A ou B da seção 1)
#    Tools > Build Game (Windows), ou o comando de linha de comando da seção 1.2

# 3. Religue e reinicie o servidor dedicado com o binário novo
Enable-ScheduledTask -TaskName "TOP-DedicatedServer"            # requer PowerShell elevada
Start-ScheduledTask -TaskName "TOP-DedicatedServer"

# 4. Publique no itch.io
cd "Tools\Release"
.\Publish-Itch.ps1 -BuildDir "..\..\Build\GameProjectKG" -ItchUser "kg-online" -ItchGame "kg-online"
cd "C:\Users\klebe\Tales of Pirates Unity"

# 5. Suba o código para o GitHub
git add -A
git commit -m "descrição da mudança

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
$ghToken = & "C:\Program Files\GitHub CLI\gh.exe" auth token
git push "https://x-access-token:$ghToken@github.com/kleberkunha1-eng/Tales-of-Pirates-Unity.git" main
```

---

## 6. Checar se está tudo no ar

Comandos rápidos de diagnóstico, um por componente:

```powershell
# Servidor do jogo rodando e escutando a porta certa?
Get-Process | Where-Object { $_.ProcessName -like "*GameProjectKG*" } | Select-Object Id, StartTime
netstat -ano | Select-String ":7777"

# Tarefa agendada ativa?
Get-ScheduledTask -TaskName "TOP-DedicatedServer" | Select-Object TaskName, State

# Túnel playit.gg ativo?
Get-Service playitd | Select-Object Name, Status, StartType

# API de produção respondendo?
Invoke-RestMethod -Uri "https://gamekg.pages.dev/api/health"

# Build no itch.io processada?
cd "Tools\Release"; .\butler\butler.exe status "kg-online/kg-online:windows"

# Logs do servidor (últimas linhas)
Get-Content "Tools\Release\logs\server.out.log" -Tail 30
```

---

## 7. Problemas comuns

| Sintoma | Causa provável | Solução |
|---|---|---|
| Build falha com `UnauthorizedAccessException: ... dstorage.dll` | O jogo (servidor dedicado ou cliente) ainda está rodando e travando os arquivos | Pare o processo (`Stop-Process`) e a tarefa agendada antes de buildar (seção 2.4) |
| Build falha com `error CS...` no log | Erro de compilação C# | Abra o Editor, veja o Console, corrija o código |
| Jogador recebe "servidor multiplayer não configurado" | `Tools\server-address.txt` não existia na hora do build, ou o `api.json` da build ficou com `gameServerHost` vazio | Crie/atualize `Tools\server-address.txt` com `host:porta` do túnel e **gere a build de novo** |
| Erro "UNAUTHORIZED" em qualquer ação (criar/carregar personagem, admin, etc.) | Token JWT do jogador não chegou até a chamada da API (bug corrigido em 05/10/2026 — token agora é guardado por conexão em `PlayerConnection.SessionToken`) | Confirme que está rodando a build mais recente; se persistir, veja `TOPNetworkManager.cs` (`FinalizeAuthentication`) e `DatabaseService.cs` |
| `Stop-Process`/`Enable-ScheduledTask` dá "Acesso negado" | Comando precisa de PowerShell elevada (administrador) | Rode `Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -Command "..."'` |
| `git push` pede usuário/senha e trava | Prompt de credencial interativo não funciona neste ambiente | Use o token do `gh.exe` embutido na URL (seção 4.2), nunca `git push origin main` direto |
| Túnel playit.gg caiu / jogadores não conseguem conectar | Serviço `playitd` parado, ou o túnel foi recriado com endereço diferente | `Get-Service playitd` → `Start-Service playitd` se parado; se o endereço mudou, atualize `Tools\server-address.txt` e gere build nova |
