# Windows Security - SmartScreen, Defender e falsos positivos

Este documento explica como diagnosticar e reportar corretamente um falso positivo de
antivirus/SmartScreen nos arquivos deste jogo. O pipeline de release **nunca** tenta contornar
essas protecoes (ver regras abaixo) - a unica solucao legitima e assinatura + reputacao + canal
oficial de submissao de falsos positivos.

## O que este projeto NUNCA faz

- Desativar o Windows Defender ou o SmartScreen.
- Adicionar excecoes automaticas no Defender como parte da distribuicao publica (o Launcher tem
  um botao opcional, acionado manualmente pelo jogador com confirmacao de administrador, para a
  pasta especifica do jogo - nunca acontece "por tras" sem interacao do usuario).
- Remover o Mark-of-the-Web (`Zone.Identifier`) de arquivos no computador do jogador (nunca usa
  `Unblock-File` como parte da instalacao).
- Alterar configuracoes de seguranca do Windows ou do registro.
- Usar packers, ofuscadores (UPX ou similares) para "esconder" o executavel de antivirus - isso
  so aumenta a chance de deteccao heuristica, nao diminui.

## Duas camadas diferentes (nao confundir)

1. **SmartScreen (reputacao de aplicativo)**: um filtro de reputacao, nao um antivirus. Mostra um
   aviso "O Windows protegeu o computador" com a opcao **Mais informacoes > Executar assim mesmo**
   - nao apaga nem altera o arquivo. A reputacao se constroi organicamente conforme mais pessoas
   baixam e executam o aplicativo sem reportar problema - isso e normal para qualquer app novo,
   assinado ou nao.
2. **Antivirus em tempo real (Defender ou de terceiros)**: deteccao heuristica/comportamental que
   pode de fato colocar em quarentena, apagar ou alterar bytes do arquivo. Nao tem um botao
   simples de "executar assim mesmo" - requer restaurar/excluir manualmente nas configuracoes do
   antivirus. **Se o jogador usa um antivirus de terceiros (Avast, Kaspersky, Norton, AVG, etc.),
   a excecao automatica do Launcher so funciona para o Windows Defender** - o jogador precisa
   liberar a pasta manualmente dentro do programa dele.

## Como diagnosticar um falso positivo (localmente)

1. Abra **Windows Security** (Seguranca do Windows).
2. Va em **Virus & threat protection** (Protecao contra virus e ameacas).
3. Clique em **Protection history** (Historico de protecao).
4. Procure pela deteccao relacionada ao jogo (nome do arquivo, data/hora). Anote:
   - Nome da ameaca detectada (ex.: `Trojan:Win32/Wacatac.B!ml` - geralmente indica deteccao
     heuristica baseada em comportamento, nao uma assinatura de malware conhecida).
   - Caminho completo do arquivo afetado.
   - Acao tomada (quarentena, removido, bloqueado).

## Verificar a assinatura de um arquivo suspeito

```powershell
Get-AuthenticodeSignature "C:\GameProjectKG\Game\GameProjectKG.exe" | Format-List *
```

Ou pela interface: botao direito no arquivo > **Properties** > aba **Digital Signatures**.

## Submeter um falso positivo a Microsoft

Se um arquivo legitimo deste jogo for classificado incorretamente:

1. Acesse https://www.microsoft.com/wdsi/filesubmission
2. Envie o arquivo exato (o mesmo hash SHA-256 publicado em `checksums.sha256`/
   `release-manifest.json` daquela versao).
3. Categoria: **Software developer** > **I'm the publisher of this file...** (ou similar) para
   priorizar a analise.
4. Anote o ID da submissao (ex.: `https://www.microsoft.com/en-us/wdsi/submission/<id>`) para
   acompanhar o status.
5. Aguarde a analise (pode levar de horas a poucos dias). Uma vez classificado como limpo, a
   deteccao e removida das definicoes do Defender globalmente.

## Checklist de falsos positivos antes de cada release

Verificar antes de publicar:

- [ ] Nenhum executavel inesperado foi adicionado a build (compare com a lista de arquivos do
      release anterior).
- [ ] Nenhuma DLL de terceiros foi modificada (deve manter a assinatura original do fornecedor -
      ver [ThirdPartyFiles.md](../ThirdPartyFiles.md)).
- [ ] Nenhum packer/ofuscador foi introduzido.
- [ ] O Launcher/Updater continuam assinados (quando `SIGNING_MODE` configurado) - nunca
      distribuir um updater/launcher nao assinado silenciosamente.
- [ ] Nenhum processo baixa e executa codigo arbitrario sem verificar hash (o patcher sempre
      confere SHA-256 antes de substituir arquivos - ver `Launcher/patcher.js`).
- [ ] Nenhum spawn desnecessario de PowerShell (o unico uso de PowerShell elevado no Launcher e
      a excecao manual e opcional do Defender, acionada pelo jogador).
- [ ] Nenhuma injecao de DLL, manipulacao de processo de terceiros ou mecanismo de persistencia
      incomum (o jogo nao se registra para iniciar com o Windows, nao cria servicos, etc.).

Se algum desses pontos falhar, **reporte e analise o impacto antes de remover a funcionalidade**
- nao remova automaticamente sem entender a causa.
