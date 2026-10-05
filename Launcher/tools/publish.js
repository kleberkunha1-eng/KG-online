// Publica uma build do Unity como atualizacao.
// Uso: node tools/publish.js "<pasta da build>" [--version 1.0.5] [--notes "texto"] [--exe Jogo.exe] [--out pasta]
// Sem --out e com publish.config.json: envia os arquivos para o GitHub Release (por hash) e o manifest para o repositorio do site (deploy automatico no Cloudflare Pages).
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const cp = require('child_process');
const os = require('os');

const args = process.argv.slice(2);
const opt = n => { const i = args.indexOf('--' + n); return i >= 0 ? args[i + 1] : null; };
const src = args[0] && !args[0].startsWith('--') ? path.resolve(args[0]) : null;
if (!src || !fs.existsSync(src)) { console.error('Uso: npm run publish-update -- "<pasta da build>" [--version X] [--notes "..."]'); process.exit(1); }

let cfg = null;
try { cfg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'publish.config.json'), 'utf8')); } catch { }
const remote = !opt('out') && !!cfg;
const out = remote ? path.join(cfg.repoDir, 'public', 'patch') : path.resolve(opt('out') || path.join(__dirname, '..', '..', 'API', 'patch'));
const GH = remote ? (cfg.gh || 'gh') : null;
const gh = (...a) => cp.execFileSync(GH, a, { encoding: 'utf8', maxBuffer: 1 << 26, stdio: ['ignore', 'pipe', 'inherit'] });
const TAG = 'files';
const filesDir = path.join(out, 'files');
fs.mkdirSync(remote ? out : filesDir, { recursive: true });

const IGNORE = [/_BackUpThisFolder_ButDontShipItWithYourGame/i, /_BurstDebugInformation_DoNotShip/i, /\.pdb$/i, /^UnityCrashHandler/i];
function walk(dir, rel = '') {
    let r = [];
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        const rp = rel ? rel + '/' + e.name : e.name;
        if (IGNORE.some(rx => rx.test(e.name))) continue;
        if (e.isDirectory()) r = r.concat(walk(path.join(dir, e.name), rp));
        else r.push(rp);
    }
    return r;
}
const sha = f => new Promise((res, rej) => {
    const h = crypto.createHash('sha256');
    fs.createReadStream(f).on('data', c => h.update(c)).on('end', () => res(h.digest('hex'))).on('error', rej);
});

(async () => {
    let old = { build: 0, files: [] };
    try { old = JSON.parse(fs.readFileSync(path.join(out, 'manifest.json'), 'utf8')); } catch { }
    const oldMap = new Map(old.files.map(f => [f.path, f]));

    const exes = fs.readdirSync(src).filter(n => /\.exe$/i.test(n) && !/^UnityCrashHandler/i.test(n));
    const exe = opt('exe') || exes[0];
    if (!exe || !fs.existsSync(path.join(src, exe))) { console.error('Nao achei o .exe do jogo na pasta. Use --exe Nome.exe'); process.exit(1); }

    // Arquivos no GitHub Release: nome = sha256 (conteudo nao muda, entao so sobe o que e novo).
    let have = new Set();
    let base = '';
    if (remote) {
        gh('auth', 'status');
        try { gh('release', 'view', TAG, '--repo', cfg.githubRepo); }
        catch { gh('release', 'create', TAG, '--repo', cfg.githubRepo, '--title', 'Arquivos do jogo', '--notes', 'Arquivos do jogo (nomeados pelo hash). Gerenciado pelo publicador do launcher.'); }
        have = new Set(JSON.parse(gh('release', 'view', TAG, '--repo', cfg.githubRepo, '--json', 'assets')).assets.map(a => a.name));
        base = `https://github.com/${cfg.githubRepo}/releases/download/${TAG}/`;
    }

    const files = [];
    let copied = 0;
    const tmp = remote ? fs.mkdtempSync(path.join(os.tmpdir(), 'pub-')) : null;
    for (const rel of walk(src)) {
        const abs = path.join(src, rel);
        const size = fs.statSync(abs).size;
        const hash = await sha(abs);
        const entry = { path: rel, size, sha256: hash };
        if (remote && size > 0) {
            entry.url = base + hash;
            if (!have.has(hash)) {
                const up = path.join(tmp, hash);
                fs.copyFileSync(abs, up);
                console.log(`Enviando ${rel} (${Math.round(size / 1048576)} MB)...`);
                gh('release', 'upload', TAG, up, '--repo', cfg.githubRepo, '--clobber');
                fs.rmSync(up, { force: true });
                have.add(hash);
                copied++;
            }
        } else if (!remote) {
            const dest = path.join(filesDir, ...rel.split('/'));
            const prev = oldMap.get(rel);
            if (!prev || prev.sha256 !== hash || !fs.existsSync(dest)) {
                fs.mkdirSync(path.dirname(dest), { recursive: true });
                fs.copyFileSync(abs, dest);
                copied++;
            }
        }
        files.push(entry);
    }
    if (tmp) fs.rmSync(tmp, { recursive: true, force: true });

    const keep = new Set(files.map(f => f.path));
    let removed = 0;
    for (const f of old.files) if (!keep.has(f.path)) { if (!remote) fs.rmSync(path.join(filesDir, ...f.path.split('/')), { force: true }); removed++; }

    const build = (old.build || 0) + 1;
    const manifest = {
        version: opt('version') || `1.0.${build}`, build, exe, notes: opt('notes') || '',
        publishedAt: new Date().toISOString(), files,
    };
    fs.writeFileSync(path.join(out, 'manifest.json'), JSON.stringify(manifest));
    console.log(`Publicado ${manifest.version}: ${files.length} arquivos (${copied} novos/alterados, ${removed} removidos).`);

    if (remote) {
        // Remove do release os arquivos que nenhuma versao usa mais (limite de 1000 assets por release).
        const used = new Set(files.map(f => f.sha256));
        for (const name of have) if (!used.has(name) && /^[0-9a-f]{64}$/.test(name)) {
            try { gh('release', 'delete-asset', TAG, name, '--repo', cfg.githubRepo, '--yes'); } catch { }
        }
        const git = (...a) => cp.execFileSync('git', ['-C', cfg.repoDir, ...a], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] });
        git('add', 'public/patch/manifest.json');
        git('-c', 'user.name=launcher-publisher', '-c', 'user.email=publisher@users.noreply.github.com', 'commit', '-m', `Update ${manifest.version}`);
        const tok = gh('auth', 'token').trim();
        cp.execFileSync('git', ['-C', cfg.repoDir, '-c', 'credential.helper=', 'push', `https://x-access-token:${tok}@github.com/${cfg.githubRepo}.git`, 'main'], { stdio: ['ignore', 'ignore', 'inherit'] });
        console.log('Manifest enviado ao GitHub; o Cloudflare Pages publica em ~1 minuto.');
    } else {
        console.log('Saida: ' + out);
    }
})().catch(e => { console.error('ERRO: ' + e.message); process.exit(1); });