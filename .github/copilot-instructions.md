# Instrucoes para o GitHub Copilot neste repositorio

Este e um projeto Unity (C#) de grande porte (remake do cliente PKO "Tales of Pirates").
Para manter respostas rapidas e o consumo de tokens baixo, siga estas regras:

## Escopo de analise

- Analise **exclusivamente** arquivos de codigo-fonte `.cs` (scripts C#) e arquivos de
  configuracao em texto simples (`.json`, `.md`, `.yml`, `.txt` pequenos) quando
  explicitamente necessario para a tarefa.
- **Nao leia e nao solicite o conteudo** de arquivos `.meta`, `.asset`, `.prefab`,
  `.unity` ou de qualquer binario do Unity (`.fbx`, `.obj`, `.png`, `.jpg`, `.tga`,
  `.psd`, `.wav`, `.mp3`, `.ogg`, `.ttf`, `.otf`, `.anim`, `.controller`, `.mat`).
  Esses arquivos nao sao texto util para analise de codigo e consomem muito contexto.
- Nunca varra as pastas `Library/`, `Temp/`, `Logs/`, `Obj/`, `Build/`, `Builds/`,
  `UserSettings/` ou `MemoryCaptures/` - sao geradas automaticamente pelo Unity/pelo
  processo de build e nunca contem codigo-fonte relevante.
- Se precisar inspecionar um prefab ou uma cena (`.prefab`/`.unity`), prefira usar as
  ferramentas de automacao do Editor (scripts em `Assets/Editor/`) em vez de ler o YAML
  bruto do arquivo inteiro; se a leitura direta for mesmo necessaria, leia apenas o
  trecho relevante, nunca o arquivo inteiro.

## Estilo de resposta

- Responda de forma **direta e concisa**, evitando repeticoes e explicacoes
  desnecessarias, para economizar tokens de saida.
- Priorize mostrar o codigo/diff relevante em vez de descrever em prosa longa o que
  foi mudado.
