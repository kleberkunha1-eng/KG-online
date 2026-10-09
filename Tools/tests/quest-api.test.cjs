const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { DatabaseSync } = require('node:sqlite');
const { test } = require('node:test');

const root = path.resolve(__dirname, '..', '..');
const apiRequire = createRequire(path.join(root, 'API', 'package.json'));
const cloudRequire = createRequire(path.join(root, 'Cloudflare', 'package.json'));
const { Hono } = cloudRequire('hono');
const jwt = apiRequire('jsonwebtoken');
const secret = 'isolated-quest-fixture-not-a-real-credential';

function database() {
    const db = new DatabaseSync(':memory:');
    db.exec(fs.readFileSync(path.join(root, 'Cloudflare', 'migrations', '0001_init.sql'), 'utf8'));
    const migration = fs.readFileSync(path.join(root, 'Cloudflare', 'migrations', '0003_quest_progress.sql'), 'utf8');
    db.exec(migration);
    const token = jwt.sign({ sub: 9 }, secret, { expiresIn: '1h' });
    db.prepare('INSERT INTO accounts (id, username, email, password_hash, session_token, session_expires) VALUES (9, ?, ?, ?, ?, ?)')
        .run('fixture', 'fixture@example.invalid', 'unused', token, Math.floor(Date.now() / 1000) + 3600);
    db.prepare('INSERT INTO accounts (id, username, email, password_hash) VALUES (10, ?, ?, ?)')
        .run('other', 'other@example.invalid', 'unused');
    db.prepare('INSERT INTO characters (id, account_id, slot_index, name, gold) VALUES (100, 9, 0, ?, 123)')
        .run('FixtureOwner');
    db.prepare('INSERT INTO characters (id, account_id, slot_index, name) VALUES (101, 10, 0, ?)')
        .run('OtherOwner');
    db.prepare('INSERT INTO characters (id, account_id, slot_index, name, is_deleted) VALUES (102, 9, 1, ?, 1)')
        .run('DeletedOwner');
    function statement(sql, params) {
        return {
            async first() { return db.prepare(sql).get(...params) || null; },
            async all() { return { results: db.prepare(sql).all(...params) }; },
            async run() {
                const result = db.prepare(sql).run(...params);
                return { meta: { changes: Number(result.changes), last_row_id: Number(result.lastInsertRowid) } };
            },
        };
    }
    return {
        db, migration, token,
        prepare(sql) { return { bind(...params) { return statement(sql, params); } }; },
        async execute(sql, params) {
            sql = sql.replaceAll('NOW()', 'unixepoch()').replace('INSERT IGNORE', 'INSERT OR IGNORE')
                .replace('GREATEST(', 'MAX(');
            if (/^\s*SELECT/i.test(sql)) return [db.prepare(sql).all(...params)];
            const result = await statement(sql, params).run();
            return [{ affectedRows: result.meta.changes }];
        },
    };
}

async function lifecycle(call, fixture) {
    assert.deepEqual(await call('GET'), { success: true, active: [], completed: [] });
    for (const charId of [101, 102, 999])
        for (const action of ['', 'accept', 'progress', 'abandon', 'complete'])
            assert.equal((await call(action ? 'POST' : 'GET', action, { questId: 721, progress: 2 }, charId)).error,
                'CHARACTER_NOT_FOUND');
    for (const charId of ['bad', '100oops', -1, 0, '1.5'])
        assert.equal((await call('GET', '', undefined, charId)).error, 'INVALID_CHARACTER');
    for (const questId of [null, false, 0, -1, 1.5, '721', '721oops', 2147483648, [], {}])
        for (const action of ['accept', 'progress', 'abandon', 'complete'])
            assert.equal((await call('POST', action, { questId, progress: 2 })).error, 'INVALID_QUEST');
    assert.deepEqual(await call('POST', 'complete', { questId: 721 }),
        { success: false, error: 'QUEST_NOT_ACTIVE' });
    const accepted = await Promise.all([call('POST', 'accept', { questId: 721 }),
        call('POST', 'accept', { questId: 721 })]);
    assert.equal(accepted.filter(result => result.success).length, 1);
    assert.equal(accepted.filter(result => result.error === 'ALREADY_HAVE_QUEST').length, 1);
    for (const progress of [null, false, -1, 1.5, '2', 2147483648])
        assert.equal((await call('POST', 'progress', { questId: 721, progress })).error, 'INVALID_QUEST');
    assert.equal((await call('POST', 'progress', { questId: 721, progress: 10 })).success, true);
    assert.equal((await call('POST', 'progress', { questId: 721, progress: 3 })).success, true);
    assert.equal((await call('POST', 'progress', { questId: 721, progress: 0 })).success, true);
    assert.deepEqual((await call('GET')).active, [{ questId: 721, progress: 10 }]);
    fixture.db.exec(fixture.migration);
    assert.deepEqual((await call('GET')).active, [{ questId: 721, progress: 10 }]);
    const completed = await Promise.all([call('POST', 'complete', { questId: 721 }),
        call('POST', 'complete', { questId: 721 })]);
    assert.equal(completed.filter(result => result.success).length, 1);
    const timestamp = fixture.db.prepare('SELECT completed_at FROM quest_progress WHERE quest_id = 721').get().completed_at;
    assert.ok(timestamp > 0);
    for (const action of ['complete', 'abandon', 'progress'])
        assert.equal((await call('POST', action, { questId: 721, progress: 100 })).error, 'QUEST_NOT_ACTIVE');
    assert.equal((await call('POST', 'accept', { questId: 721 })).error, 'ALREADY_HAVE_QUEST');
    assert.deepEqual(await call('GET'), { success: true, active: [], completed: [721] });
    assert.equal(fixture.db.prepare('SELECT completed_at FROM quest_progress WHERE quest_id = 721').get().completed_at, timestamp);
    assert.equal((await call('POST', 'accept', { questId: 722 })).success, true);
    assert.equal((await call('POST', 'abandon', { questId: 722 })).success, true);
    assert.equal((await call('POST', 'accept', { questId: 722 })).success, true);
    assert.deepEqual((await call('GET')).active, [{ questId: 722, progress: 0 }]);
    assert.equal(fixture.db.prepare('SELECT gold FROM characters WHERE id = 100').get().gold, 123);
}

test('Node quest routes: ownership, validation, concurrent requests, monotonic progress and non-destructive migration', async () => {
    const fixture = database(), routes = new Map();
    const app = { use() {}, get(route, handler) { routes.set('GET ' + route, handler); },
        post(route, handler) { routes.set('POST ' + route, handler); }, put() {}, delete() {} };
    apiRequire('./game.js')(app, fixture);
    try {
        await lifecycle(async (method, action = '', body, charId = 100) => {
            let output;
            const response = { json(value) { output = value; return this; }, status() { return this; } };
            const route = '/api/game/quests/:charId' + (action ? '/' + action : '');
            await routes.get(method + ' ' + route)({ body, params: { charId: String(charId) }, user: { id: 9 } }, response);
            return output;
        }, fixture);
    } finally { fixture.db.close(); }
});

test('Cloudflare quest routes: authenticated requests and identical persistence contract', async () => {
    const fixture = database();
    const source = fs.readFileSync(path.join(root, 'Cloudflare', 'functions', 'api', '[[path]].js'), 'utf8')
        .replace(/^import .*;\r?$/gm, '').replace('export const onRequest', 'const onRequest');
    const context = vm.createContext({ Hono, handle: () => {}, ITEMS: {}, console, TextEncoder, TextDecoder,
        crypto: globalThis.crypto, btoa, atob, Date, Response, Request, URL });
    vm.runInContext(source + '\nglobalThis.fixtureApp = app;', context);
    const env = { DB: fixture, JWT_SECRET: secret };
    try {
        const unauthenticated = await context.fixtureApp.fetch(new Request('https://fixture.invalid/api/game/quests/100'), env);
        assert.equal(unauthenticated.status, 401);
        await lifecycle(async (method, action = '', body, charId = 100) => {
            const url = 'https://fixture.invalid/api/game/quests/' + charId + (action ? '/' + action : '');
            const response = await context.fixtureApp.fetch(new Request(url, {
                method, headers: { Authorization: 'Bearer ' + fixture.token, 'Content-Type': 'application/json' },
                body: method === 'GET' || body === undefined ? undefined : JSON.stringify(body),
            }), env);
            assert.equal(response.status, 200);
            return response.json();
        }, fixture);
    } finally { fixture.db.close(); }
});

test('MariaDB quest SQL uses temporary tables only and rejects repeated completion',
    { skip: process.env.TOP_TEST_MARIADB !== '1' }, async () => {
        apiRequire('dotenv').config({ path: [path.join(root, 'API', '.env.local'),
            path.join(root, 'API', '.env')], quiet: true });
        const host = process.env.DB_HOST || '127.0.0.1';
        assert.ok(['127.0.0.1', 'localhost', '::1'].includes(host), 'MariaDB fixture must remain local.');
        const db = await apiRequire('mysql2/promise').createConnection({
            host, port: Number(process.env.DB_PORT || 3306), user: process.env.DB_USER || 'root',
            password: process.env.DB_PASS, database: process.env.DB_NAME || 'top_unity',
        });
        try {
            await db.execute('CREATE TEMPORARY TABLE characters (id BIGINT PRIMARY KEY, account_id BIGINT, is_deleted INT DEFAULT 0)');
            await db.execute('CREATE TEMPORARY TABLE quest_progress (id INT AUTO_INCREMENT PRIMARY KEY, character_id BIGINT, quest_id INT, progress INT DEFAULT 0, completed_at DATETIME NULL, UNIQUE KEY uniq_char_quest (character_id, quest_id))');
            await db.execute('INSERT INTO characters (id, account_id) VALUES (100, 9)');
            const routes = new Map();
            const app = { use() {}, get(route, handler) { routes.set('GET ' + route, handler); },
                post(route, handler) { routes.set('POST ' + route, handler); }, put() {}, delete() {} };
            apiRequire('./game.js')(app, db);
            async function call(method, action = '', body) {
                let output;
                const response = { json(value) { output = value; return this; }, status() { return this; } };
                const route = '/api/game/quests/:charId' + (action ? '/' + action : '');
                await routes.get(method + ' ' + route)({ body, params: { charId: '100' }, user: { id: 9 } }, response);
                return output;
            }
            assert.equal((await call('POST', 'accept', { questId: 721 })).success, true);
            assert.equal((await call('POST', 'accept', { questId: 721 })).error, 'ALREADY_HAVE_QUEST');
            for (const progress of [10, 3, 0, 10])
                assert.equal((await call('POST', 'progress', { questId: 721, progress })).success, true);
            assert.deepEqual((await call('GET')).active, [{ questId: 721, progress: 10 }]);
            const completed = await Promise.all([call('POST', 'complete', { questId: 721 }),
                call('POST', 'complete', { questId: 721 })]);
            assert.equal(completed.filter(result => result.success).length, 1);
            assert.equal((await call('POST', 'abandon', { questId: 721 })).error, 'QUEST_NOT_ACTIVE');
            assert.deepEqual((await call('GET')).completed, [721]);
        } finally { await db.end(); }
    });
