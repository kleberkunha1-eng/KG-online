const $ = id => document.getElementById(id);
const fmt = b => b > 1e9 ? (b / 1e9).toFixed(2) + ' GB' : b > 1e6 ? (b / 1e6).toFixed(1) + ' MB' : Math.ceil(b / 1e3) + ' KB';
let mode = 'check';

function setMain(text, m, enabled = true) { $('main').textContent = text; $('main').disabled = !enabled; mode = m; }
function bar(p) { $('fill').style.width = Math.max(0, Math.min(100, p)) + '%'; }

window.launcher.onProgress(p => {
  if (p.phase === 'check') {
    $('status').textContent = 'Verificando arquivos...';
    bar(p.done / p.total * 100);
    $('detail').textContent = `${p.done}/${p.total}`;
  } else {
    $('status').textContent = 'Baixando atualizacao...';
    bar(p.total ? p.done / p.total * 100 : 100);
    $('detail').textContent = `${fmt(p.done)} / ${fmt(p.total)} - ${fmt(p.speed)}/s`;
  }
});

window.launcher.onOutdated(info => {
  $('outdatedBanner').hidden = false;
  $('outdatedText').textContent = `Uma nova versao do launcher esta disponivel (${info.current} -> ${info.latest}). Baixe e substitua o arquivo atual.`;
});
$('outdatedBtn').onclick = () => window.launcher.openDownload();

window.launcher.onGameExit(result => {
  $('status').textContent = 'O jogo foi encerrado inesperadamente.';
  $('detail').textContent = `Codigo: ${result.code ?? 'desconhecido'}${result.signal ? `; sinal: ${result.signal}` : ''}`;
  $('crashActions').hidden = false;
  $('crashNote').textContent = 'Um relatorio foi enviado automaticamente para o suporte.';
  if (result.missingDep) {
    $('status').textContent = 'Falta um componente do sistema (Visual C++ Redistributable) nesta maquina.';
    $('redistActions').hidden = false;
    $('redistNote').textContent = '';
  }
  if (result.winEvent) {
    $('status').textContent = 'O Windows identificou a causa: veja os detalhes abaixo.';
    $('winEventBox').hidden = false;
    $('winEventText').textContent = result.winEvent;
  }
});

async function check() {
  setMain('Verificando...', 'check', false);
  $('status').textContent = 'Verificando atualizacoes...';
  const r = await window.launcher.check();
  if (!r) return;
  if (!r.ok) {
    $('status').textContent = 'Nao foi possivel conectar ao servidor de atualizacoes.';
    $('detail').textContent = r.error; bar(0);
    return setMain('Tentar novamente', 'check');
  }
  $('notes').textContent = r.notes || '';
  if (r.upToDate) {
    $('status').textContent = `Jogo atualizado (versao ${r.latest})`; $('detail').innerHTML = '&nbsp;'; bar(100);
    return setMain('JOGAR', 'play');
  }
  bar(0);
  const first = !r.installed;
  $('status').textContent = first ? 'Jogo nao instalado.' : `Atualizacao disponivel: ${r.installed} -> ${r.latest}`;
  $('detail').textContent = `${r.files} arquivo(s), ${fmt(r.bytes)}`;
  setMain(first ? 'INSTALAR' : 'ATUALIZAR', 'update');
}

async function update() {
  setMain('Baixando...', 'update', false);
  ['dir'].forEach(i => $(i).disabled = true);
  const r = await window.launcher.update();
  $('dir').disabled = false;
  if (!r.ok) {
    $('status').textContent = 'Falha na atualizacao.'; $('detail').textContent = r.error;
    return setMain('Tentar novamente', 'check');
  }
  check();
}

async function repair() {
  setMain('Verificando...', 'check', false);
  ['dir', 'repair'].forEach(i => $(i).disabled = true);
  $('status').textContent = 'Verificando e reparando arquivos...';
  $('detail').textContent = 'A integridade de todos os arquivos sera validada.';
  bar(0);
  const r = await window.launcher.repair();
  ['dir', 'repair'].forEach(i => $(i).disabled = false);
  if (!r.ok) {
    $('status').textContent = 'Falha ao reparar a instalacao.';
    $('detail').textContent = r.error;
    return setMain('Tentar novamente', 'check');
  }
  $('status').textContent = r.repaired
    ? `Instalacao reparada (${r.files} arquivo(s)).`
    : 'Instalacao verificada: nenhum arquivo precisa de reparo.';
  $('detail').innerHTML = '&nbsp;';
  await check();
}

$('main').onclick = async () => {
  if (mode === 'check') return check();
  if (mode === 'update') return update();
  if (mode === 'play') {
    setMain('Iniciando...', 'play', false);
    $('crashActions').hidden = true;
    $('avActions').hidden = true;
    $('redistActions').hidden = true;
    $('winEventBox').hidden = true;
    const r = await window.launcher.play();
    if (!r.ok) {
      $('status').textContent = r.error; setMain('JOGAR', 'play');
      if (/antivirus/i.test(r.error)) { $('avActions').hidden = false; $('avNote').textContent = ''; }
    } else { $('status').textContent = 'Jogo iniciado. O launcher permanecera aberto.'; setMain('JOGAR', 'play'); }
  }
};
$('dir').onclick = async () => { $('path').textContent = await window.launcher.chooseDir(); check(); };
$('open').onclick = () => window.launcher.openDir();
$('repair').onclick = repair;
$('site').onclick = () => window.launcher.openSite();
$('openLogs').onclick = () => window.launcher.openLogsDir();
$('copyLog').onclick = async () => {
  const r = await window.launcher.copyCrashLog();
  $('crashNote').textContent = r.ok ? 'Log copiado! Cole (Ctrl+V) para enviar ao suporte.' : (r.error || 'Falha ao copiar o log.');
};
$('fixAv').onclick = async () => {
  $('avNote').textContent = 'Verificando antivirus instalado...';
  const avList = await window.launcher.detectAntivirus().catch(() => []);
  // A correcao automatica (Add-MpPreference) so funciona para o Windows Defender. Se houver um
  // antivirus de terceiros tambem registrado (Avast, Kaspersky, Norton, AVG, etc.), ele continuara
  // bloqueando mesmo depois da excecao no Defender - o jogador precisa liberar a pasta manualmente
  // dentro do programa dele, entao avisamos isso antes de tentar.
  const thirdParty = avList.filter(n => !/windows defender|microsoft defender/i.test(n));
  $('avNote').textContent = 'Solicitando permissao de administrador (excecao no Windows Defender)...';
  const r = await window.launcher.fixAntivirusBlock();
  let msg = r.ok
    ? 'Excecao adicionada no Windows Defender! Clique em "Verificar e reparar" e tente jogar novamente.'
    : `Nao foi possivel adicionar a excecao: ${r.error || 'solicitacao cancelada.'}`;
  if (thirdParty.length) {
    msg += ` ATENCAO: detectamos tambem "${thirdParty.join(', ')}" instalado - essa correcao automatica NAO`
      + ' funciona nele. Abra o programa do seu antivirus, procure por "Excecoes" ou "Exclusions" e adicione'
      + ' a pasta de instalacao do jogo manualmente.';
  }
  $('avNote').textContent = msg;
};
$('fixRedist').onclick = async () => {
  $('redistNote').textContent = 'Baixando e instalando (pode levar ate 1 minuto)...';
  const r = await window.launcher.installRedist();
  $('redistNote').textContent = r.ok
    ? 'Componente instalado! Tente jogar novamente.'
    : `Nao foi possivel instalar: ${r.error || 'solicitacao cancelada.'}`;
};
$('fixRedistManual').onclick = async () => {
  const prev = $('status').textContent;
  $('status').textContent = 'Instalando componentes do sistema (pode levar ate 1 minuto)...';
  const r = await window.launcher.installRedist();
  $('status').textContent = r.ok
    ? 'Componentes do sistema instalados/atualizados com sucesso.'
    : `Nao foi possivel instalar: ${r.error || 'solicitacao cancelada.'}`;
  setTimeout(() => { if ($('status').textContent.startsWith('Componentes') || $('status').textContent.startsWith('Nao foi possivel')) $('status').textContent = prev; }, 6000);
};

(async () => {
  const i = await window.launcher.info();
  $('name').textContent = i.name; $('ver').textContent = 'Launcher ' + i.version; $('path').textContent = i.installDir;
  document.title = i.name;
  check();
})();