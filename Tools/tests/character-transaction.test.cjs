const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { DatabaseSync } = require('node:sqlite');
const { test } = require('node:test');
const { randomUUID } = require('node:crypto');

const root = path.resolve(__dirname, '..', '..');
const apiRequire = createRequire(path.join(root, 'API', 'package.json'));
const cloudRequire = createRequire(path.join(root, 'Cloudflare', 'package.json'));
const secret = 'transaction-fixture-not-a-real-credential';

async function lifecycle(call, read) {
    const snapshot = {
        OperationId: randomUUID(), QuestId: 721,
        Character: { SaveRevision: 0, Level: 5, Exp: 70, Gold: 200, CurrentHp: 100, MaxHp: 100 },
        Inventory: [{ UniqueItemId: 'bbbc31e7-cd67-4f74-bad1-c9986f64f28a', SlotIndex: 0, ItemId: 1847, Quantity: 1, Durability: 17, Gem1: -1, Gem2: -1, Gem3: -1 }],
        Skills: [],
    };
    assert.equal((await call({ ...snapshot, OperationId: 'bad' })).error, 'INVALID_TRANSACTION');
    for (const QuestId of [false, null, '721', -1, 1.5, 2147483648])
        assert.equal((await call({ ...snapshot, QuestId })).error, 'INVALID_TRANSACTION');
    for (const SaveRevision of [-1, null, '0', Number.MAX_SAFE_INTEGER])
        assert.equal((await call({ ...snapshot, Character: { ...snapshot.Character, SaveRevision } })).error, 'INVALID_TRANSACTION');
    for (const invalidId of ['100oops', 0, -1, 1.5])
        assert.equal((await call(snapshot, invalidId)).error, 'INVALID_CHARACTER');
    assert.equal((await call({ ...snapshot, Inventory: null })).error, 'INVALID_TRANSACTION');
    for (const field of ['Gold', 'Exp'])
        for (const value of [null, '100', -1, 1.5, Number.MAX_SAFE_INTEGER + 1])
            assert.equal((await call({ ...snapshot, Character: { ...snapshot.Character, [field]: value } })).error,
                'INVALID_CHARACTER_VALUES');
    assert.equal((await call(snapshot, 101)).error, 'NOT_FOUND');
    const simultaneous = await Promise.all([call(snapshot), call(snapshot)]);
    assert.ok(simultaneous.every(r => r.success && r.revision === 1));
    let state = await read();
    assert.equal(Number(state.character.gold), 200);
    assert.equal(Number(state.character.save_revision), 1);
    assert.ok(state.quest.completed_at);
    assert.equal(state.inventory.length, 1);
    assert.equal(state.inventory[0].unique_item_id, snapshot.Inventory[0].UniqueItemId);
    assert.equal(state.inventory[0].durability, 17);
    assert.equal(state.receipts.length, 1);
    assert.equal((await call(snapshot)).success, true);
    assert.equal((await call({ ...snapshot, Character: { ...snapshot.Character, Gold: 999 } })).error, 'OPERATION_MISMATCH');
    assert.equal((await call({ ...snapshot, OperationId: randomUUID() })).error, 'SAVE_CONFLICT');
    assert.equal((await call({ ...snapshot, OperationId: randomUUID(), Character: { ...snapshot.Character, SaveRevision: 1 } })).error, 'QUEST_NOT_ACTIVE');
    state = await read();
    assert.equal(Number(state.character.gold), 200);
    assert.equal(state.receipts.length, 1);

    const save = { ...snapshot, QuestId: 0, OperationId: randomUUID(),
        Character: { ...snapshot.Character, SaveRevision: 1, Gold: 250 } };
    assert.equal((await call(save)).revision, 2);
    // Retry of an old acknowledged transaction must not replay its obsolete inventory or gold.
    assert.equal((await call(snapshot)).revision, 1);
    state = await read();
    assert.equal(Number(state.character.gold), 250);
    assert.equal(Number(state.character.save_revision), 2);

    const invalid = { ...snapshot, QuestId: 722, OperationId: randomUUID(),
        Character: { ...snapshot.Character, SaveRevision: 2, Gold: 1000 },
        Inventory: [
            { ...snapshot.Inventory[0], UniqueItemId: randomUUID(), SlotIndex: 0 },
            { ...snapshot.Inventory[0], UniqueItemId: randomUUID(), SlotIndex: 0 },
        ] };
    assert.equal((await call(invalid)).success, false);
    state = await read();
    assert.equal(Number(state.character.gold), 250);
    assert.equal(Number(state.character.save_revision), 2);
    assert.equal(state.quest722.completed_at, null);
    assert.equal(state.receipts.length, 2);
    assert.equal(state.inventory[0].unique_item_id, snapshot.Inventory[0].UniqueItemId);
    const finish = { ...invalid, OperationId: randomUUID(), Inventory: snapshot.Inventory };
    assert.equal((await call(finish)).revision, 3);
    state = await read();
    assert.ok(state.quest722.completed_at);
    assert.equal(Number(state.character.gold), 1000);
    assert.equal(state.receipts.length, 3);
    const reloaded = await call(null, 100, 'GET');
    assert.equal(reloaded.success, true);
    assert.equal(Number(reloaded.Character.SaveRevision), 3);
    assert.equal(Number(reloaded.Character.Gold), 1000);
    assert.equal(reloaded.Inventory.length, 1);
    assert.equal(reloaded.Inventory[0].UniqueItemId, snapshot.Inventory[0].UniqueItemId);
    assert.deepEqual(reloaded.Character.Boats, []);
    const boat = { Id: randomUUID(), Name: 'Guppy Test', TypeId: 1, BerthId: 1, Level: 1,
        HullId: 43, EngineId: 15, BowId: 8, CannonId: 53, ComponentId: 73, Health: 2280, Fuel: 200 };
    const purchase = { ...save, OperationId: randomUUID(),
        Character: { ...save.Character, SaveRevision: 3, Gold: 100, Boats: [boat] } };
    for (const Boats of [[{ ...boat, Name: 'x' }], [{ ...boat, Health: -1 }], [{ ...boat, Id: 'not-an-id' }],
        [boat, boat], Array.from({ length: 4 }, () => ({ ...boat, Id: randomUUID() }))])
        assert.equal((await call({ ...purchase, Character: { ...purchase.Character, Boats } })).error, 'INVALID_BOATS');
    assert.equal((await call(purchase)).revision, 4);
    assert.equal((await call(purchase)).revision, 4);
    state = await read();
    assert.equal(Number(state.character.gold), 100);
    assert.deepEqual(JSON.parse(state.character.boats_json), [boat]);
    assert.equal((await call({ ...purchase, Character: { ...purchase.Character, Boats: [] } })).error, 'OPERATION_MISMATCH');
    const legacySave = { ...save, OperationId: randomUUID(), Character: { ...save.Character, SaveRevision: 4, Gold: 200 } };
    assert.equal((await call(legacySave)).revision, 5);
    assert.deepEqual((await call(null, 100, 'GET')).Character.Boats, [boat], 'Old saves omitting boats preserve ownership.');
    const rollback = { ...invalid, QuestId: 0, OperationId: randomUUID(),
        Character: { ...purchase.Character, SaveRevision: 5, Boats: [{ ...boat, Id: randomUUID() }] } };
    assert.equal((await call(rollback)).success, false);
    state = await read();
    assert.deepEqual(JSON.parse(state.character.boats_json), [boat], 'Inventory failure rolls back boat replacement and funds.');
    assert.equal(Number(state.character.gold), 200);
    assert.equal(Number(state.character.save_revision), 5);
}

test('D1 atomic character/quest save: retries, stale autosave, concurrent replay and full rollback', async () => {
    const db = new DatabaseSync(':memory:');
    db.exec(fs.readFileSync(path.join(root, 'Cloudflare', 'migrations', '0001_init.sql'), 'utf8'));
    db.exec(fs.readFileSync(path.join(root, 'Cloudflare', 'migrations', '0003_quest_progress.sql'), 'utf8'));
    db.exec(fs.readFileSync(path.join(root, 'Cloudflare', 'migrations', '0004_character_save_transactions.sql'), 'utf8'));
    db.exec(fs.readFileSync(path.join(root, 'Cloudflare', 'migrations', '0006_boat_ownership.sql'), 'utf8'));
    const token = apiRequire('jsonwebtoken').sign({ sub: 9 }, secret, { expiresIn: '1h' });
    db.prepare('INSERT INTO accounts (id,username,email,password_hash,session_token,session_expires) VALUES (9,?,?,?,?,?)')
        .run('fixture', 'fixture@example.invalid', 'unused', token, Math.floor(Date.now() / 1000) + 3600);
    db.exec("INSERT INTO characters (id,account_id,slot_index,name,gold) VALUES (100,9,0,'Fixture',123)");
    db.exec('INSERT INTO quest_progress(character_id,quest_id) VALUES (100,721),(100,722)');
    const adapter = {
        prepare(sql) {
            return { bind(...params) {
                return { sql, params, async first() { return db.prepare(sql).get(...params) || null; },
                    async all() { return { results: db.prepare(sql).all(...params) }; } };
            } };
        },
        async batch(statements) {
            db.exec('BEGIN');
            try {
                const results = statements.map(s => {
                    const r = db.prepare(s.sql).run(...s.params);
                    return { meta: { changes: Number(r.changes) } };
                });
                db.exec('COMMIT');
                return results;
            } catch (e) { db.exec('ROLLBACK'); throw e; }
        },
    };
    const source = fs.readFileSync(path.join(root, 'Cloudflare', 'functions', 'api', '[[path]].js'), 'utf8')
        .replace(/^import .*;\r?$/gm, '').replace('export const onRequest', 'const onRequest');
    const errors = [];
    const context = vm.createContext({ Hono: cloudRequire('hono').Hono, handle: () => {}, ITEMS: {},
        console: { error(...args) { errors.push(args.join(' ')); } }, TextEncoder, TextDecoder,
        crypto: globalThis.crypto, btoa, atob, Date, Response, Request, URL });
    vm.runInContext(source + '\nglobalThis.fixtureApp = app;', context);
    try {
        await lifecycle(async (snapshot, id = 100, method = 'PUT') => {
            const response = await context.fixtureApp.fetch(new Request('https://fixture.invalid/api/game/characters/' + id,
                { method, headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
                    body: method === 'PUT' ? JSON.stringify(snapshot) : undefined }), { DB: adapter, JWT_SECRET: secret });
            return response.json();
        }, async () => ({
            character: db.prepare('SELECT * FROM characters WHERE id=100').get(),
            quest: db.prepare('SELECT * FROM quest_progress WHERE quest_id=721').get(),
            quest722: db.prepare('SELECT * FROM quest_progress WHERE quest_id=722').get(),
            inventory: db.prepare('SELECT * FROM inventory WHERE character_id=100').all(),
            receipts: db.prepare('SELECT * FROM character_save_receipts').all(),
        }));
        assert.equal(errors.length, 2, 'Only intentional invalid-slot transactions report database errors.');
    } finally { db.close(); }
});

test('MariaDB atomic character/quest save uses only temporary tables',
    { skip: process.env.TOP_TEST_MARIADB !== '1' }, async () => {
        apiRequire('dotenv').config({ path: [path.join(root, 'API', '.env.local'), path.join(root, 'API', '.env')], quiet: true });
        const host = process.env.DB_HOST || '127.0.0.1';
        assert.ok(['127.0.0.1', 'localhost', '::1'].includes(host));
        const db = await apiRequire('mysql2/promise').createConnection({ host,
            port: Number(process.env.DB_PORT || 3306), user: process.env.DB_USER || 'root',
            password: process.env.DB_PASS, database: process.env.DB_NAME || 'top_unity' });
        try {
            for (const table of ['characters', 'inventory', 'skills', 'quest_progress']) {
                const [ddl] = await db.execute(`SHOW CREATE TABLE ${table}`);
                await db.query(ddl[0]['Create Table'].replace('CREATE TABLE', 'CREATE TEMPORARY TABLE')
                    .replace(/^\s*CONSTRAINT[^\n]+\n/gm, '').replace(/,\n\)/g, '\n)'));
            }
            const [columns] = await db.execute("SHOW COLUMNS FROM characters LIKE 'save_revision'");
            if (!columns.length) await db.execute('ALTER TABLE characters ADD COLUMN save_revision BIGINT NOT NULL DEFAULT 0');
            await db.query(fs.readFileSync(path.join(root, 'API', 'migrations', '0006_boat_ownership.sql'), 'utf8'));
            await db.execute('CREATE TEMPORARY TABLE character_save_receipts (character_id BIGINT NOT NULL, operation_id VARCHAR(36) NOT NULL, payload_hash CHAR(64) NOT NULL, expected_revision BIGINT NOT NULL, quest_id INT NOT NULL DEFAULT 0, PRIMARY KEY(character_id,operation_id))');
            await db.execute("INSERT INTO characters(id,account_id,slot_index,name,gold) VALUES (100,9,0,'Fixture',123)");
            await db.execute('INSERT INTO quest_progress(character_id,quest_id) VALUES(100,721),(100,722)');
            const routes = new Map();
            const app = { use() {}, get(route, handler) { routes.set('GET:' + route, handler); }, post() {}, delete() {},
                put(route, handler) { routes.set(route, handler); } };
            // All operations are serialized on the single connection which owns the temporary tables.
            let tail = Promise.resolve();
            const adapter = { execute: (...args) => db.execute(...args), async getConnection() { return {
                beginTransaction: () => db.beginTransaction(), execute: (...args) => db.execute(...args),
                commit: () => db.commit(), rollback: () => db.rollback(), release() {},
            }; } };
            apiRequire(path.join(root, 'API', 'game.js'))(app, adapter);
            await lifecycle((snapshot, id = 100, method = 'PUT') => {
                const result = tail.then(async () => {
                    let output;
                    const response = { json(value) { output = value; return this; }, status() { return this; } };
                    await routes.get((method === 'GET' ? 'GET:' : '') + '/api/game/characters/:id')
                        ({ body: snapshot, params: { id }, user: { id: 9 } }, response);
                    return output;
                });
                tail = result.then(() => {}, () => {});
                return result;
            }, async () => {
                const query = async sql => (await db.execute(sql))[0];
                return { character: (await query('SELECT * FROM characters WHERE id=100'))[0],
                    quest: (await query('SELECT * FROM quest_progress WHERE quest_id=721'))[0],
                    quest722: (await query('SELECT * FROM quest_progress WHERE quest_id=722'))[0],
                    inventory: await query('SELECT * FROM inventory WHERE character_id=100'),
                    receipts: await query('SELECT * FROM character_save_receipts') };
            });
        } finally { await db.end(); }
    });
