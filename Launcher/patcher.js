// Logica de instalacao/atualizacao (sem dependencia do Electron, testavel via node).
const fs = require('fs');
const fsp = fs.promises;
const path = require('path');
const crypto = require('crypto');
const { Readable } = require('stream');
const { pipeline } = require('stream/promises');

const STATE_FILE = '.launcher-state.json';
const CONCURRENCY = 4;

function safeRel(rel) {
    const n = path.normalize(rel);
    if (path.isAbsolute(n) || n.startsWith('..') || n.includes(':')) throw new Error('Caminho invalido no manifest: ' + rel);
    return n;
}

async function fetchJson(url) {
    const res = await fetch(url + (url.includes('?') ? '&' : '?') + 't=' + Date.now(), { cache: 'no-store' });
    if (!res.ok) throw new Error(`Servidor respondeu ${res.status} (${url})`);
    return res.json();
}

async function hashFile(file) {
    const h = crypto.createHash('sha256');
    await pipeline(fs.createReadStream(file), h);
    return h.digest('hex');
}

async function readState(dir) {
    try { return JSON.parse(await fsp.readFile(path.join(dir, STATE_FILE), 'utf8')); }
    catch { return { version: null, build: 0, files: {} }; }
}

async function writeState(dir, st) {
    await fsp.mkdir(dir, { recursive: true });
    await fsp.writeFile(path.join(dir, STATE_FILE), JSON.stringify(st));
}

// Compara manifest x disco. state.files guarda {size, mtimeMs, sha256} para nao re-hashear tudo.
async function check(patchUrl, dir, onProgress, options = {}) {
    const manifest = await fetchJson(patchUrl.replace(/\/$/, '') + '/manifest.json');
    const state = await readState(dir);
    const need = [];
    let i = 0;
    for (const f of manifest.files) {
        const rel = safeRel(f.path);
        const abs = path.join(dir, rel);
        i++;
        if (onProgress) onProgress({ phase: 'check', done: i, total: manifest.files.length, file: rel });
        let st;
        try { st = await fsp.stat(abs); } catch { need.push(f); continue; }
        if (st.size !== f.size) { need.push(f); continue; }
        const c = state.files[rel];
        if (!options.forceHash && c && c.mtimeMs === st.mtimeMs && c.ctimeMs === st.ctimeMs && c.size === st.size && c.sha256 === f.sha256) continue;
        const sha = await hashFile(abs);
        if (sha !== f.sha256) { need.push(f); continue; }
        state.files[rel] = { size: st.size, mtimeMs: st.mtimeMs, ctimeMs: st.ctimeMs, sha256: sha };
    }
    const keep = new Set(manifest.files.map(f => safeRel(f.path)));
    const remove = Object.keys(state.files).filter(r => !keep.has(r));
    await writeState(dir, state);
    return {
        manifest, need, remove,
        bytes: need.reduce((a, f) => a + f.size, 0),
        installed: state.version,
        upToDate: need.length === 0 && state.version === manifest.version,
    };
}

async function downloadOne(patchUrl, dir, f, onBytes) {
    const rel = safeRel(f.path);
    const abs = path.join(dir, rel);
    const part = abs + '.part';
    await fsp.mkdir(path.dirname(abs), { recursive: true });
    if (f.size === 0) { await fsp.writeFile(abs, ''); return; }
    if (f.url && !/^https:\/\//i.test(f.url)) throw new Error('URL invalida no manifest');
    const url = f.url || (patchUrl.replace(/\/$/, '') + '/files/' + rel.split(path.sep).map(encodeURIComponent).join('/'));
    let last;
    for (let attempt = 1; attempt <= 3; attempt++) {
        let counted = 0;
        try {
            const res = await fetch(url, { cache: 'no-store' });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const hash = crypto.createHash('sha256');
            const src = Readable.fromWeb(res.body);
            src.on('data', c => { hash.update(c); counted += c.length; onBytes(c.length); });
            await pipeline(src, fs.createWriteStream(part));
            if (hash.digest('hex') !== f.sha256) throw new Error('Hash invalido');
            await fsp.rename(part, abs);
            return;
        } catch (e) {
            onBytes(-counted);
            last = e;
            await fsp.rm(part, { force: true });
            if (attempt < 3) await new Promise(r => setTimeout(r, 1000 * attempt));
        }
    }
    throw new Error(`Falha ao baixar ${rel}: ${last.message}`);
}

async function update(patchUrl, dir, plan, onProgress) {
    await fsp.mkdir(dir, { recursive: true });
    const { manifest, need, remove } = plan;
    const total = plan.bytes;
    let done = 0;
    const queue = need.slice();
    const t0 = Date.now();
    const emit = file => onProgress && onProgress({
        phase: 'download', done, total, file, speed: done / Math.max(1, (Date.now() - t0) / 1000),
    });
    const worker = async () => {
        while (queue.length) {
            const f = queue.shift();
            emit(f.path);
            await downloadOne(patchUrl, dir, f, n => { done += n; emit(f.path); });
        }
    };
    await Promise.all(Array.from({ length: Math.min(CONCURRENCY, need.length) }, worker));

    for (const rel of remove) await fsp.rm(path.join(dir, safeRel(rel)), { force: true });

    const state = await readState(dir);
    for (const rel of remove) delete state.files[rel];
    for (const f of manifest.files) {
        const rel = safeRel(f.path);
        const st = await fsp.stat(path.join(dir, rel));
        state.files[rel] = { size: st.size, mtimeMs: st.mtimeMs, ctimeMs: st.ctimeMs, sha256: f.sha256 };
    }
    state.version = manifest.version;
    state.build = manifest.build;
    state.exe = manifest.exe;
    await writeState(dir, state);
}

module.exports = { check, update, readState, safeRel, hashFile };