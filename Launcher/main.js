const { app, BrowserWindow, ipcMain, dialog, shell, clipboard } = require('electron');
const path = require('path');
const fs = require('fs');
const os = require('os');
const { spawn } = require('child_process');
const patcher = require('./patcher');

const portableDir = app.isPackaged ? path.dirname(process.execPath) : __dirname;
const configPath = app.isPackaged
    ? path.join(process.resourcesPath, 'launcher.config.json')
    : path.join(portableDir, 'launcher.config.json');
const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
const settingsFile = path.join(app.getPath('userData'), 'settings.json');
let win;
let plan = null;
let busy = false;

function loadSettings() {
    try { return JSON.parse(fs.readFileSync(settingsFile, 'utf8')); } catch { return {}; }
}
function saveSettings(s) {
    fs.mkdirSync(path.dirname(settingsFile), { recursive: true });
    fs.writeFileSync(settingsFile, JSON.stringify(s));
}
function defaultInstallDir() {
    // Preferencia: instalar direto na raiz do disco C:\ (ex.: C:\GameProjectKG\Game).
    const systemDrive = process.env.SystemDrive || 'C:';
    const root = systemDrive.endsWith('\\') ? systemDrive : systemDrive + '\\';
    try {
        fs.accessSync(root, fs.constants.W_OK);
        return path.join(root, 'GameProjectKG', 'Game');
    } catch {
        // Sem permissao de escrita na raiz do disco (ex.: usuario nao-admin): usa pasta do usuario.
        return path.join(process.env.LOCALAPPDATA || app.getPath('userData'), 'GameProjectKG', 'Game');
    }
}
function installDir() {
    return loadSettings().installDir || defaultInstallDir();
}

// Codigos de saida do jogo que tipicamente indicam dependencia de sistema faltando (nao bug no build):
// 0xC000007B (3221225595) = STATUS_INVALID_IMAGE_FORMAT, o famoso "0xc000007b" - geralmente falta o
// Microsoft Visual C++ Redistributable x64. Esse tipo de erro acontece no carregador do Windows, ANTES
// do Unity rodar qualquer codigo, entao o -logFile nunca chega a ser escrito (log vazio e esperado aqui).
const MISSING_DEPENDENCY_CODES = new Set([3221225595, 3221226306]); // 0xC000007B, 0xC0000142

// Heuristica leve (sem child_process/registry) para saber se o runtime VC++ x64 provavelmente esta
// presente: o instalador sempre coloca essas DLLs em System32 em qualquer Windows com o redist x64.
function vcRedistLikelyInstalled() {
    try {
        const sys32 = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32');
        return fs.existsSync(path.join(sys32, 'vcruntime140.dll')) && fs.existsSync(path.join(sys32, 'vcruntime140_1.dll'));
    } catch { return false; }
}

// Pasta onde o launcher guarda o log do jogo (o Unity escreve nela via -logFile) para poder
// ser lida/enviada apos um fechamento inesperado (ex.: codigo 3221225477 = access violation).
const logsDir = path.join(app.getPath('userData'), 'logs');
let lastLogFile = null;
let lastMetaFile = null;
function newGameLogPath() {
    fs.mkdirSync(logsDir, { recursive: true });
    const files = fs.readdirSync(logsDir).filter(n => n.startsWith('game-') && (n.endsWith('.log') || n.endsWith('.meta.txt'))).sort();
    for (const old of files.slice(0, -8)) fs.rm(path.join(logsDir, old), { force: true }, () => { }); // mantem so os pares mais recentes
    return path.join(logsDir, `game-${Date.now()}.log`);
}
function readLogTail(file, maxChars = 60000) {
    try {
        const txt = fs.readFileSync(file, 'utf8');
        return txt.length > maxChars ? txt.slice(-maxChars) : txt;
    } catch { return ''; }
}
// Combina o log do Unity (pode estar vazio/ausente se o jogo falhou antes de a engine iniciar) com o
// arquivo de diagnostico do proprio launcher (sempre existe: horario, executavel, evento do Windows).
function readCombinedLog(logFile, metaFile) {
    const meta = metaFile ? readLogTail(metaFile) : '';
    const game = logFile ? readLogTail(logFile) : '';
    if (!meta && !game) return '';
    return [meta && `--- Diagnostico do launcher ---\n${meta}`, game && `--- Log do Unity ---\n${game}`].filter(Boolean).join('\n\n');
}
function osInfo() {
    return `${os.platform()} ${os.release()} (${os.arch()}) - ${os.cpus()[0]?.model || '?'} - RAM ${Math.round(os.totalmem() / 1073741824)}GB`;
}
// A placa de video/driver costuma ser a causa de crashes dificeis de reproduzir (driver desatualizado,
// incompatibilidade com DirectX 11/12). Diferente do Log de Eventos, essa informacao nunca falha/e
// suprimida pelo Windows, entao serve como dado confiavel mesmo quando o resto do diagnostico falha.
function gpuInfo() {
    return new Promise(resolve => {
        const ps = `(Get-CimInstance Win32_VideoController | Select-Object -First 4 Name, DriverVersion | ForEach-Object { "$($_.Name) (driver $($_.DriverVersion))" }) -join ' | '`;
        const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { windowsHide: true });
        let out = '';
        child.stdout && child.stdout.on('data', d => { out += String(d); });
        child.once('error', () => resolve('?'));
        child.once('exit', () => resolve(out.trim() || '?'));
        setTimeout(() => { try { child.kill(); } catch { } resolve(out.trim() || '?'); }, 6000);
    });
}

// Erros como 0xc000007b/0xc0000005 no carregamento de um .exe acontecem ANTES do Unity rodar qualquer
// linha de codigo, entao -logFile nunca e escrito (nao e um bug do launcher - a engine nunca chega a
// inicializar seu proprio sistema de log). A unica fonte de diagnostico disponivel nesses casos e o
// proprio Windows, que registra dois tipos de evento diferentes dependendo da causa:
//  - "Application Popup" (ID 26, log "System"): DLL ausente/corrompida/incompativel ao carregar o .exe
//    (o que cobre 0xc000007b, 0xc0000135 etc.) - inclui o nome exato do modulo culpado.
//  - "Application Error"/"Windows Error Reporting" (ID 1000, log "Application"): falha durante a
//    execucao (ex.: access violation 0xc0000005) - tambem inclui o "Faulting module" exato.
// A versao anterior so verificava o primeiro tipo, por isso nao capturava nada em crashes durante a
// execucao. Agora consulta os dois logs e tambem tenta novamente por alguns segundos, ja que o Windows
// pode demorar um pouco para gravar o evento (especialmente apos um access violation).
function getWindowsCrashEvent(exeName) {
    const ps = `
        try {
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8;
            $since = (Get-Date).AddMinutes(-5);
            $esc = [regex]::Escape('${exeName}');
            $a = @(Get-WinEvent -LogName Application -ErrorAction SilentlyContinue |
                Where-Object { $_.TimeCreated -gt $since -and $_.Id -eq 1000 -and $_.Message -match $esc });
            $b = @(Get-WinEvent -LogName System -ErrorAction SilentlyContinue |
                Where-Object { $_.TimeCreated -gt $since -and $_.ProviderName -eq 'Application Popup' -and $_.Message -match $esc });
            ($a + $b) | Sort-Object TimeCreated -Descending | Select-Object -First 2 -ExpandProperty Message
        } catch {
            Write-Output "ERRO ao consultar o Visualizador de Eventos: $($_.Exception.Message)"
        }
    `;
    const runOnce = () => new Promise(resolve => {
        const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { windowsHide: true });
        let out = '';
        child.stdout && child.stdout.on('data', d => { out += String(d); });
        child.once('error', () => resolve(''));
        child.once('exit', () => resolve(out.trim()));
        setTimeout(() => { try { child.kill(); } catch { } resolve(out.trim()); }, 8000);
    });
    // O Windows pode levar alguns segundos para gravar o evento apos o processo terminar (o WER roda em
    // segundo plano); tenta varias vezes antes de desistir, em vez de uma unica consulta imediata.
    return (async () => {
        for (const waitMs of [1500, 3000, 4000, 5000]) {
            await new Promise(r => setTimeout(r, waitMs));
            const result = await runOnce();
            if (result) return result;
        }
        return '';
    })();
}

// O launcher e um .exe portatil: ele NAO se autoatualiza como o jogo (que e verificado por manifest/hash
// a cada abertura). Uma vez baixado, o jogador continua rodando o mesmo codigo para sempre, a menos que
// baixe o arquivo de novo - por isso jogadores com uma copia antiga do launcher nao recebem correcoes
// (ex.: captura de log de crash, deteccao de bloqueio do antivirus) ate baixar a versao atual do site.
// Para nao depender disso silenciosamente, o launcher consulta um JSON simples hospedado no site e avisa
// na propria tela quando ha uma versao mais nova, com um botao que abre a pagina de download.
function versionNewer(remote, local) {
    const a = String(remote).split('.').map(n => parseInt(n, 10) || 0);
    const b = String(local).split('.').map(n => parseInt(n, 10) || 0);
    for (let i = 0; i < Math.max(a.length, b.length); i++) {
        const x = a[i] || 0, y = b[i] || 0;
        if (x !== y) return x > y;
    }
    return false;
}
async function checkLauncherUpdate() {
    try {
        const res = await fetch(config.siteUrl.replace(/\/+$/, '') + '/launcher/version.json', { cache: 'no-store' });
        if (!res.ok) return;
        const info = await res.json();
        if (info && info.version && versionNewer(info.version, app.getVersion())) {
            send('launcher-outdated', { latest: info.version, current: app.getVersion(), notes: info.notes || '' });
        }
    } catch { /* checagem e melhor-esforco; falha de rede nao deve incomodar o jogador */ }
}
async function reportCrash({ exitCode, exitSignal, logFile, metaFile }) {
    try {
        const st = await patcher.readState(installDir());
        await fetch(config.siteUrl.replace(/\/+$/, '') + '/api/crash-report', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                launcherVersion: app.getVersion(),
                gameVersion: st.version || '',
                exitCode: exitCode ?? null,
                exitSignal: exitSignal || '',
                osInfo: osInfo(),
                log: readCombinedLog(logFile, metaFile),
            }),
        });
    } catch { /* envio e melhor-esforco; falha de rede nao deve incomodar o jogador */ }
}

const send = (ch, d) => win && !win.isDestroyed() && win.webContents.send(ch, d);

function createWindow() {
    win = new BrowserWindow({
        width: 960, height: 580, resizable: false, backgroundColor: '#0b0f1a', title: config.gameName,
        autoHideMenuBar: true,
        webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false },
    });
    win.removeMenu();
    win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
}

ipcMain.handle('info', () => ({ name: config.gameName, installDir: installDir(), siteUrl: config.siteUrl, version: app.getVersion() }));

ipcMain.handle('check', async () => {
    if (busy) return null;
    busy = true;
    try {
        plan = await patcher.check(config.patchUrl, installDir(), p => send('progress', p));
        return {
            ok: true, upToDate: plan.upToDate, installed: plan.installed, latest: plan.manifest.version,
            bytes: plan.bytes, files: plan.need.length, notes: plan.manifest.notes || '',
        };
    } catch (e) {
        plan = null;
        return { ok: false, error: e.message };
    } finally { busy = false; }
});

ipcMain.handle('update', async () => {
    if (busy || !plan) return { ok: false, error: 'Verifique as atualizacoes primeiro.' };
    busy = true;
    try {
        const wasFirstInstall = !plan.installed;
        await patcher.update(config.patchUrl, installDir(), plan, p => send('progress', p));
        plan = null;
        if (wasFirstInstall) {
            // Roda em sequencia (nao em paralelo) para nao abrir duas janelas de UAC ao mesmo tempo.
            (async () => {
                // Apos a primeira instalacao, tenta instalar o VC++ Redistributable em silencio (pede UAC
                // uma vez). Isso evita que o jogador so descubra a falta dessa dependencia ao clicar em
                // JOGAR (erro 0xc000007b).
                if (!vcRedistLikelyInstalled()) await installRedistInternal().catch(() => { });
                // Tambem adiciona a pasta como excecao do Windows Defender logo apos instalar, ANTES do
                // jogador tentar abrir o jogo pela primeira vez. Encontramos em logs reais de jogadores
                // arquivos do jogo (DLLs e assets) com o MESMO tamanho mas hash diferente do esperado -
                // sinal de que o antivirus estava alterando/neutralizando o conteudo apos o download (o
                // .exe nao e assinado digitalmente). Fazer isso no primeiro instante reduz a chance do
                // antivirus chegar a tocar nos arquivos antes da excecao entrar em vigor.
                await fixAntivirusBlockInternal(installDir()).catch(() => { });
            })();
        }
        return { ok: true };
    } catch (e) {
        return { ok: false, error: e.message };
    } finally { busy = false; }
});

ipcMain.handle('repair', async () => {
    if (busy) return { ok: false, error: 'Uma operacao ja esta em andamento.' };
    busy = true;
    try {
        const repairPlan = await patcher.check(
            config.patchUrl,
            installDir(),
            p => send('progress', p),
            { forceHash: true },
        );
        if (repairPlan.need.length === 0 && repairPlan.remove.length === 0) {
            return { ok: true, repaired: false, files: 0 };
        }

        await patcher.update(config.patchUrl, installDir(), repairPlan, p => send('progress', p));
        plan = null;
        return { ok: true, repaired: true, files: repairPlan.need.length + repairPlan.remove.length };
    } catch (e) {
        return { ok: false, error: e.message };
    } finally { busy = false; }
});

ipcMain.handle('play', async () => {
    try {
        const dir = installDir();
        const st = await patcher.readState(dir);
        const gameExe = st.exe;
        if (!gameExe) return { ok: false, error: 'Jogo nao instalado. Clique em INSTALAR e aguarde terminar.' };
        const abs = path.join(dir, patcher.safeRel(gameExe));
        if (!fs.existsSync(abs)) return { ok: false, error: `Executavel nao encontrado: ${abs}. Tente reinstalar o jogo.` };
        // IMPORTANTE: usa forceHash (confere o conteudo real de cada arquivo, nao so tamanho/data
        // salvos em cache) antes de iniciar o jogo. Encontramos em logs reais de jogadores arquivos
        // com o MESMO tamanho/data mas hash diferente do esperado (provavel antivirus alterando o
        // conteudo sem mudar o carimbo de data) - a checagem rapida (sem forceHash) nao detectava isso
        // e deixava o jogador tentar abrir um jogo com arquivos corrompidos, sempre resultando em
        // crash. So custa ~2-5s a mais no clique de JOGAR, e evita exatamente esse problema.
        let currentPlan = await patcher.check(config.patchUrl, dir, null, { forceHash: true });
        if (!currentPlan.upToDate) {
            // Tenta corrigir sozinho em vez de so avisar o jogador: adiciona a excecao do antivirus
            // (se ainda nao foi feita) e baixa de novo os arquivos que nao batem, depois confere outra
            // vez. Isso resolve o caso observado nos logs sem exigir que o jogador entenda o problema
            // ou clique em botoes extras. Os eventos 'progress' ja existentes (fase 'check'/'download')
            // mantém a tela com feedback visual durante essa correcao automatica.
            const settings = loadSettings();
            if (!settings.avExclusionAttempted) {
                await fixAntivirusBlockInternal(dir).catch(() => { });
                settings.avExclusionAttempted = Date.now();
                saveSettings(settings);
            }
            try {
                await patcher.update(config.patchUrl, dir, currentPlan, p => send('progress', p));
            } catch (e) {
                plan = currentPlan;
                return { ok: false, error: `Falha ao corrigir arquivos automaticamente: ${e.message}. Clique em ATUALIZAR e aguarde a conclusao.` };
            }
            currentPlan = await patcher.check(config.patchUrl, dir, null, { forceHash: true });
            if (!currentPlan.upToDate) {
                plan = currentPlan;
                return {
                    ok: false,
                    error: `Arquivos do jogo continuam corrompidos apos tentativa automatica de correcao (${currentPlan.need.length} arquivo(s): ${currentPlan.need.slice(0, 5).map(f => f.path).join(', ')}). Isso costuma ser o antivirus alterando os arquivos repetidamente - verifique se a pasta ${dir} esta na lista de excecoes do seu antivirus, ou tente temporariamente desativa-lo e clicar em ATUALIZAR.`,
                };
            }
        }

        const logFile = newGameLogPath();
        const metaFile = logFile.replace(/\.log$/, '.meta.txt');
        lastLogFile = logFile;
        lastMetaFile = metaFile;
        // Grava um arquivo separado (nao o -logFile do Unity, que ele mesmo sobrescreve ao abrir) com
        // informacoes basicas desde o instante do clique em JOGAR, para que "copiar log" NUNCA fique
        // vazio mesmo quando o jogo falha antes do Unity conseguir escrever seu proprio log (ex.: erro
        // de carregamento de DLL, que acontece antes de qualquer linha de codigo da engine rodar).
        try {
            fs.writeFileSync(metaFile, `Tentativa de inicio: ${new Date().toISOString()}\nExecutavel: ${abs}\nSistema: ${osInfo()}\nLauncher: ${app.getVersion()}\n`);
            gpuInfo().then(g => { try { fs.appendFileSync(metaFile, `Placa de video: ${g}\n`); } catch { } });
        } catch { }
        return await new Promise(resolve => {
            let started = false;
            let child;
            try {
                // -logFile manda o proprio Unity escrever o log de execucao/crash num lugar fixo que o
                // launcher controla, em vez do caminho padrao escondido em AppData/LocalLow.
                child = spawn(abs, ['--api=' + String(config.siteUrl).replace(/\/+$/, ''), '-logFile', logFile], {
                    cwd: dir, detached: true, stdio: 'ignore', windowsHide: false,
                });
            } catch (spawnError) {
                return resolve({ ok: false, error: spawnStartError(spawnError) });
            }
            child.once('error', error => resolve({ ok: false, error: spawnStartError(error) }));
            child.once('spawn', () => {
                started = true;
                child.unref();
                resolve({ ok: true });
            });
            child.once('exit', (code, signal) => {
                if (started && code !== 0) {
                    const missingDep = MISSING_DEPENDENCY_CODES.has(code);
                    try { fs.appendFileSync(metaFile, `\nProcesso encerrado: ${new Date().toISOString()}\nCodigo de saida: ${code}${signal ? ` (sinal: ${signal})` : ''}\n`); } catch { }
                    (async () => {
                        // O Log de Eventos do Windows e util quando existe, mas o proprio Windows pode
                        // suprimir notificacoes repetidas do mesmo erro em um curto intervalo (throttling
                        // da WER) - confirmado em testes locais apos varias falhas seguidas. Por isso nao
                        // e a unica fonte: tambem reconfere a integridade dos arquivos instalados contra o
                        // manifest (mesma logica do botao "Verificar e reparar"), que nunca falha/e
                        // suprimida e aponta diretamente um arquivo corrompido, se for o caso.
                        try {
                            const recheck = await patcher.check(config.patchUrl, dir, null, { forceHash: true });
                            const integridade = (recheck.need.length === 0)
                                ? 'OK - todos os arquivos instalados batem com o manifest (nao e corrupcao/antivirus removendo arquivo).'
                                : `PROBLEMA - ${recheck.need.length} arquivo(s) nao batem com o manifest: ${recheck.need.slice(0, 10).map(f => f.path).join(', ')}`;
                            fs.appendFileSync(metaFile, `\nVerificacao de integridade pos-falha: ${integridade}\n`);
                        } catch (e) {
                            try { fs.appendFileSync(metaFile, `\nVerificacao de integridade pos-falha: falhou (${e.message}).\n`); } catch { }
                        }
                        // getWindowsCrashEvent ja espera o tempo necessario internamente (o WER pode demorar
                        // alguns segundos para gravar o evento apos o processo terminar).
                        const winEvent = await getWindowsCrashEvent(path.basename(abs));
                        if (winEvent) {
                            try { fs.appendFileSync(metaFile, '\n=== Evento do Windows (diagnostico automatico) ===\n' + winEvent + '\n'); } catch { }
                        } else {
                            try { fs.appendFileSync(metaFile, '\nNenhum evento de erro do Windows foi encontrado para este processo (o Visualizador de Eventos as vezes suprime avisos repetidos do mesmo erro).\n'); } catch { }
                        }
                        send('game-exit', { code, signal, logFile, metaFile, missingDep, winEvent });
                        reportCrash({ exitCode: code, exitSignal: signal, logFile, metaFile });
                    })();
                }
            });
        });
    } catch (error) {
        return { ok: false, error: `Falha ao iniciar o jogo: ${error.message}` };
    }
});

// Codigos como EFTYPE/UNKNOWN indicam executavel invalido/corrompido no disco. A causa mais comum
// nao e download incompleto, e sim o antivirus (Windows Defender/SmartScreen) colocando o .exe em
// quarentena por ele nao ser assinado digitalmente - orienta o jogador a reparar e, se persistir,
// a liberar a pasta no antivirus em vez de mostrar erro tecnico cru.
function spawnStartError(error) {
    const code = error && error.code;
    if (code === 'EFTYPE' || code === 'UNKNOWN' || code === 'ENOENT' || code === 'EACCES') {
        plan = null;
        return 'O arquivo do jogo esta corrompido, incompleto ou foi colocado em quarentena pelo antivirus. '
            + 'Clique em "Verificar e reparar"; se o erro continuar, clique em "Corrigir bloqueio do antivirus".';
    }
    return `Nao foi possivel iniciar o jogo: ${error.message}`;
}

// Detecta qual(is) antivirus estao registrados no Windows (via WMI SecurityCenter2). Isso importa
// porque a correcao automatica abaixo (Add-MpPreference) SO funciona para o Windows Defender - se o
// jogador tiver instalado um antivirus de terceiros (Avast, Kaspersky, Norton, AVG, etc.), esse
// comando nao tem efeito nenhum nele, e o jogador precisa abrir o programa dele e liberar a pasta
// manualmente. Retorna a lista de nomes de produtos encontrados (ex.: ["Windows Defender"],
// ["Avast Free Antivirus", "Windows Defender"]).
function detectAntivirusInternal() {
    return new Promise(resolve => {
        const ps = "Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntivirusProduct | Select-Object -ExpandProperty displayName";
        const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { windowsHide: true });
        let out = '';
        child.stdout && child.stdout.on('data', d => { out += String(d); });
        child.once('error', () => resolve([]));
        child.once('exit', () => {
            const names = out.split(/\r?\n/).map(s => s.trim()).filter(Boolean);
            resolve(names);
        });
    });
}
ipcMain.handle('detectAntivirus', () => detectAntivirusInternal());

// Abre uma janela elevada (UAC) que adiciona a pasta de instalacao como excecao no Windows Defender.
// Resolve o caso comum de antivirus alterando/neutralizando arquivos do jogo por eles nao serem
// assinados digitalmente (observado em logs reais: DLLs e arquivos de dados com o MESMO tamanho mas
// hash diferente do esperado - assinatura classica de um antivirus "sanitizando" o conteudo em vez de
// so deletar o arquivo).
// IMPORTANTE: isso so cria excecao no Windows Defender. Antivirus de terceiros (Avast, Kaspersky,
// Norton, AVG, etc.) tem suas proprias listas de excecao, inacessiveis por este comando - o jogador
// precisa adicionar a excecao manualmente dentro do programa dele (ver detectAntivirusInternal acima).
function fixAntivirusBlockInternal(dir) {
    return new Promise(resolve => {
        const psInner = `Add-MpPreference -ExclusionPath '${dir.replace(/'/g, "''")}'`;
        const outer = `Start-Process powershell -ArgumentList '-NoProfile -Command ${psInner.replace(/'/g, "''")}' -Verb RunAs -Wait`;
        const child = spawn('powershell.exe', ['-NoProfile', '-WindowStyle', 'Hidden', '-Command', outer], { windowsHide: true });
        let errOut = '';
        child.stderr && child.stderr.on('data', d => { errOut += String(d); });
        child.once('error', e => resolve({ ok: false, error: e.message }));
        child.once('exit', code => {
            if (code === 0) resolve({ ok: true });
            else resolve({ ok: false, error: errOut || 'O usuario pode ter cancelado a solicitacao de administrador.' });
        });
    });
}
ipcMain.handle('fixAntivirusBlock', () => fixAntivirusBlockInternal(installDir()));

// Baixa (via GitHub Releases, mesmo mecanismo usado para os arquivos do jogo) e instala em modo
// silencioso o Microsoft Visual C++ Redistributable x64 - dependencia que o Unity/Burst/Mono exigem
// e que normalmente so esta presente em maquinas de desenvolvedor (Visual Studio a instala de brinde).
// Resolve o erro "0xc000007b" (codigo 3221225595), que acontece no carregador do Windows antes do
// jogo sequer iniciar, em maquinas de jogador que nunca instalaram Visual Studio/outro redist.
const redistUrl = 'https://github.com/kleberkunha1-eng/gamekg/releases/download/launcher/vc_redist.x64.exe';
async function installRedistInternal() {
    const tmp = path.join(app.getPath('temp'), 'vc_redist.x64.exe');
    if (!fs.existsSync(tmp) || fs.statSync(tmp).size < 1024 * 1024) {
        const res = await fetch(redistUrl);
        if (!res.ok) return { ok: false, error: `Nao foi possivel baixar o componente (HTTP ${res.status}).` };
        const buf = Buffer.from(await res.arrayBuffer());
        fs.writeFileSync(tmp, buf);
    }
    return await new Promise(resolve => {
        const psInner = `Start-Process '${tmp.replace(/'/g, "''")}' -ArgumentList '/install','/quiet','/norestart' -Verb RunAs -Wait`;
        const child = spawn('powershell.exe', ['-NoProfile', '-WindowStyle', 'Hidden', '-Command', psInner], { windowsHide: true });
        let errOut = '';
        child.stderr && child.stderr.on('data', d => { errOut += String(d); });
        child.once('error', e => resolve({ ok: false, error: e.message }));
        child.once('exit', code => {
            const s = loadSettings();
            s.vcRedistAttempted = Date.now();
            saveSettings(s);
            if (code === 0) resolve({ ok: true });
            else resolve({ ok: false, error: errOut || 'O usuario pode ter cancelado a solicitacao de administrador.' });
        });
    });
}
ipcMain.handle('installRedist', async () => {
    try { return await installRedistInternal(); }
    catch (e) { return { ok: false, error: e.message }; }
});

ipcMain.handle('chooseDir', async () => {
    const r = await dialog.showOpenDialog(win, { properties: ['openDirectory', 'createDirectory'], title: 'Pasta de instalacao' });
    if (r.canceled || !r.filePaths[0]) return installDir();
    const s = loadSettings();
    s.installDir = path.join(r.filePaths[0], 'GameProjectKG');
    saveSettings(s);
    plan = null;
    return s.installDir;
});

ipcMain.handle('openDir', () => shell.openPath(installDir()));
ipcMain.handle('openSite', () => shell.openExternal(config.siteUrl));
ipcMain.handle('openDownload', () => shell.openExternal(config.siteUrl.replace(/\/+$/, '') + '/launcher.exe'));
ipcMain.handle('openLogsDir', () => { fs.mkdirSync(logsDir, { recursive: true }); return shell.openPath(logsDir); });
ipcMain.handle('copyCrashLog', () => {
    if (!lastLogFile && !lastMetaFile) return { ok: false, error: 'Nenhum log disponivel ainda. Clique em JOGAR pelo menos uma vez.' };
    const text = readCombinedLog(lastLogFile, lastMetaFile);
    if (!text) return { ok: false, error: 'Log vazio ou nao encontrado.' };
    clipboard.writeText(text);
    return { ok: true };
});
app.whenReady().then(() => { createWindow(); checkLauncherUpdate(); });
app.on('window-all-closed', () => app.quit());