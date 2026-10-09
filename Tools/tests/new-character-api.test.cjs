const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { test } = require('node:test');

const root = path.resolve(__dirname, '..', '..');
const apiRequire = createRequire(path.join(root, 'API', 'package.json'));
const cloudRequire = createRequire(path.join(root, 'Cloudflare', 'package.json'));
const jwt = apiRequire('jsonwebtoken');
const { Hono } = cloudRequire('hono');

function database() {
    const characters = [];
    function select(sql, values) {
        if (sql.includes('FROM accounts')) return [{ id: 9, username: 'fixture', is_admin: 0, is_banned: 0 }];
        if (!sql.includes('FROM characters')) {
            if (sql.includes('FROM inventory') || sql.includes('FROM skills')) return [];
            throw new Error('Unexpected fixture query: ' + sql);
        }
        let rows = characters;
        if (sql.includes('WHERE id = ?')) rows = rows.filter(row => row.id === values[0] && row.account_id === values[1]);
        else if (sql.includes('WHERE name = ?')) rows = rows.filter(row => row.name === values[0]);
        else if (sql.includes('slot_index = ?')) rows = rows.filter(row => row.account_id === values[0] && row.slot_index === values[1]);
        else if (sql.includes('WHERE account_id = ?')) rows = rows.filter(row => row.account_id === values[0]);
        return sql.includes('COUNT(*)') ? [{ c: rows.length }] : rows;
    }
    function insert(sql, values) {
        assert.match(sql, /INSERT INTO characters/);
        const columns = sql.match(/INSERT INTO characters\s*\(([^)]+)\)/)[1].split(',').map(column => column.trim());
        const expressions = sql.match(/VALUES\s*\(([\s\S]+)\)/)[1].split(',').map(value => value.trim());
        const row = { id: characters.length + 1, level: 1, gold: 0, exp: 0, equipped: '' };
        let index = 0;
        columns.forEach((column, position) => {
            row[column] = expressions[position] === '?' ? values[index++] : expressions[position] === '1' ? 1 : null;
        });
        assert.equal(index, values.length);
        characters.push(row);
        return { insertId: row.id, meta: { last_row_id: row.id } };
    }
    return {
        characters,
        async execute(sql, values) { return [sql.includes('INSERT INTO') ? insert(sql, values) : select(sql, values)]; },
        prepare(sql) {
            return {
                bind(...values) {
                    return {
                        async first() { return select(sql, values)[0] || null; },
                        async all() { return { results: select(sql, values) }; },
                        async run() { return insert(sql, values); },
                    };
                },
            };
        },
    };
}

async function lifecycle(call, db) {
    const stats = [
        [15, 10, 12, 8, 10, 150, 50, 100],
        [10, 15, 10, 10, 12, 120, 60, 100],
        [8, 10, 10, 15, 10, 100, 80, 100],
        [10, 12, 10, 12, 11, 110, 70, 100],
        [15, 10, 12, 8, 10, 150, 50, 100],
    ];
    for (let race = 0; race < 5; race++) {
        const response = await call('POST', '/api/game/characters', {
            name: 'Fixture' + race, slot: race, job: race, gender: race === 2 || race === 3 ? 1 : 0,
            hairStyle: 2, faceStyle: 1, city: { map: 'garner', x: 4, y: 1.5, z: 7 },
        });
        assert.equal(response.success, true);
        const row = db.characters[race];
        assert.equal(row.job, race);
        assert.deepEqual(['base_str', 'base_agi', 'base_con', 'base_spr', 'base_sta', 'max_hp', 'max_mp', 'max_sp']
            .map(key => row[key]), stats[race]);
        assert.equal(row.current_hp, row.max_hp);
        assert.equal(row.current_mp, row.max_mp);
        assert.equal(row.current_sp, row.max_sp);
    }
    const list = await call('GET', '/api/game/characters');
    assert.equal(list.success, true);
    const character = list.Characters.find(character => character.Job === 4);
    assert.equal(character.Name, 'Fixture4');
    assert.equal(character.Gender, 0);
    assert.equal(character.HairStyle, 2);
    assert.equal(character.FaceStyle, 1);
    const selected = await call('GET', '/api/game/characters/:id', undefined, character.Id);
    assert.equal(selected.success, true);
    assert.equal(selected.Character.Job, 4);
    assert.equal(selected.Character.CurrentHp, 150);
    assert.equal(selected.Character.BaseStr, 15);
    assert.deepEqual(Array.from(selected.Inventory), []);
    assert.equal((await call('POST', '/api/game/characters', { name: 'Another', slot: 4, job: 4 })).error, 'SLOT_OCCUPIED');
}

test('Node API creates/selects NewCharacterTest with Lance stats and preserves four original races', async () => {
    const routes = new Map();
    const db = database();
    const app = { use() {}, get(route, handler) { routes.set('GET ' + route, handler); },
        post(route, handler) { routes.set('POST ' + route, handler); },
        put() {}, delete() {} };
    apiRequire('./game.js')(app, db);
    await lifecycle(async (method, route, body, id) => {
        let output;
        const response = { json(value) { output = value; return this; }, status() { return this; } };
        await routes.get(method + ' ' + route)({ body, params: { id }, user: { id: 9 }, method, path: route }, response);
        return output;
    }, db);
});

test('Cloudflare API authenticates, creates/selects NewCharacterTest and preserves all base stats', async () => {
    const db = database();
    const secret = 'isolated-fixture-secret-not-a-real-credential';
    const source = fs.readFileSync(path.join(root, 'Cloudflare', 'functions', 'api', '[[path]].js'), 'utf8')
        .replace(/^import .*;\r?$/gm, '')
        .replace('export const onRequest', 'const onRequest');
    const context = vm.createContext({ Hono, handle: () => {}, ITEMS: {}, console, TextEncoder, TextDecoder,
        crypto: globalThis.crypto, btoa, atob, Date, Response, Request, URL });
    vm.runInContext(source + '\nglobalThis.fixtureApp = app;', context);
    const token = jwt.sign({ sub: 9 }, secret, { expiresIn: '1h' });
    await lifecycle(async (method, route, body, id) => {
        const url = 'https://fixture.invalid' + route.replace(':id', id);
        const response = await context.fixtureApp.fetch(new Request(url, {
            method, headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
            body: body === undefined ? undefined : JSON.stringify(body),
        }), { DB: db, JWT_SECRET: secret });
        assert.equal(response.status, 200);
        return response.json();
    }, db);
});
