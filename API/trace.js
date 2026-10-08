// Trace de desenvolvimento da API: tempo de cada request e das queries SQL.
// Arquivos em API/logs/trace-AAAA-MM-DD.log (mesmo formato do trace da Unity: segundos, -, CATEGORIA, mensagem).
// Desliga com API_TRACE=0. Rotaciona em 20 MB e mantem no maximo 10 arquivos.
// Nao grava corpo, parametros SQL nem headers (sem senhas/tokens no arquivo).
const fs = require('fs');
const path = require('path');
const { AsyncLocalStorage } = require('async_hooks');

const enabled = process.env.API_TRACE !== '0';
const dir = path.join(__dirname, 'logs');
const SLOW_REQ_MS = 500, SLOW_SQL_MS = 50, MAX_FILE = 20 * 1024 * 1024, KEEP = 10;
const store = new AsyncLocalStorage();
const t0 = process.hrtime.bigint();
let stream = null, file = null, size = 0, part = 0;

const now = () => Number(process.hrtime.bigint() - t0) / 1e6;
const clean = s => String(s).replace(/[\t\r\n]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 300);

function open() {
    fs.mkdirSync(dir, { recursive: true });
    const day = new Date().toISOString().slice(0, 10);
    file = path.join(dir, `trace-${day}${part ? '-' + part : ''}-api.log`);
    size = fs.existsSync(file) ? fs.statSync(file).size : 0;
    stream = fs.createWriteStream(file, { flags: 'a' });
    const old = fs.readdirSync(dir).filter(f => /^trace-.*\.log$/.test(f))
        .map(f => ({ f, t: fs.statSync(path.join(dir, f)).mtimeMs })).sort((a, b) => b.t - a.t).slice(KEEP);
    for (const o of old) try { fs.unlinkSync(path.join(dir, o.f)); } catch { }
}

function write(cat, msg) {
    if (!enabled) return;
    try {
        if (!stream || !fs.existsSync(file)) open();      // arquivo apagado pelo menu Tools > Trace
        else if (size >= MAX_FILE) { stream.end(); part++; open(); }
        const line = `${(now() / 1000).toFixed(3)}\t-\t${cat}\t${msg}\n`;
        size += Buffer.byteLength(line);
        stream.write(line);
    } catch { }
}

// Normaliza ids na rota para agrupar (/api/characters/123 -> /api/characters/:n).
const route = url => url.split('?')[0].replace(/\/\d+(?=\/|$)/g, '/:n');

function middleware(req, res, next) {
    if (!enabled) return next();
    const ctx = { sql: 0, sqlMs: 0, started: now() };
    res.on('finish', () => {
        const ms = now() - ctx.started;
        const cat = res.statusCode >= 500 ? 'REQ-ERR' : ms >= SLOW_REQ_MS ? 'REQ-SLOW' : 'REQ';
        write(cat, `${req.method} ${route(req.originalUrl)} status=${res.statusCode} ms=${ms.toFixed(1)} sql=${ctx.sql} sqlMs=${ctx.sqlMs.toFixed(1)} kb=${((+res.getHeader('content-length') || 0) / 1024).toFixed(1)}`);
        if (ms >= SLOW_REQ_MS) console.warn(`[Trace] Request lenta: ${req.method} ${route(req.originalUrl)} ${ms.toFixed(0)}ms (sql ${ctx.sql}x ${ctx.sqlMs.toFixed(0)}ms)`);
    });
    store.run(ctx, next);
}

function timeSql(target, method, label) {
    const original = target[method];
    if (typeof original !== 'function' || original.__traced) return;
    const wrapped = async function (sql, ...rest) {
        if (!enabled) return original.call(this, sql, ...rest);
        const started = now();
        try { return await original.call(this, sql, ...rest); }
        finally {
            const ms = now() - started, ctx = store.getStore();
            if (ctx) { ctx.sql++; ctx.sqlMs += ms; }
            if (ms >= SLOW_SQL_MS) write('SQL-SLOW', `${label} ms=${ms.toFixed(1)} sql=${clean(typeof sql === 'string' ? sql : sql && sql.sql)}`);
        }
    };
    wrapped.__traced = true;
    target[method] = wrapped;
}

// Mede pool.execute/query e as conexoes obtidas com getConnection.
function instrumentPool(pool) {
    if (!enabled) return pool;
    timeSql(pool, 'execute', 'pool');
    timeSql(pool, 'query', 'pool');
    const getConnection = pool.getConnection.bind(pool);
    pool.getConnection = async (...a) => {
        const started = now();
        const conn = await getConnection(...a);
        const waited = now() - started;
        if (waited >= SLOW_SQL_MS) write('SQL-SLOW', `getConnection espera ms=${waited.toFixed(1)} (pool cheio?)`);
        timeSql(conn, 'execute', 'conn');
        timeSql(conn, 'query', 'conn');
        return conn;
    };
    return pool;
}

function summary() {
    const m = process.memoryUsage();
    write('SUMMARY', `rssMB=${(m.rss / 1048576).toFixed(0)} heapMB=${(m.heapUsed / 1048576).toFixed(0)} uptime=${process.uptime().toFixed(0)}s`);
}

if (enabled) {
    write('SESSION', `start ${new Date().toISOString()} role=api node=${process.version} pid=${process.pid}`);
    setInterval(summary, 60000).unref();
    // Event loop travado (equivalente ao STALL da Unity).
    let last = now(), first = true;
    setInterval(() => { const t = now(), lag = t - last - 1000; last = t; if (!first && lag >= 200) write('STALL', `event loop travado ms=${lag.toFixed(0)}`); first = false; }, 1000).unref();
}

module.exports = { middleware, instrumentPool, write };
