// API publica do site (/api/v1) - mesmos contas e banco do jogo.
const fs = require('fs');
const path = require('path');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const rateLimit = require('express-rate-limit');

const CLASSES = {
    0: 'Novice', 1: 'Swordsman', 2: 'Hunter', 4: 'Explorer', 5: 'Herbalist', 8: 'Champion', 9: 'Crusader',
    10: 'Crusader', 11: 'Sharpshooter', 12: 'Sharpshooter', 13: 'Cleric', 14: 'Seal Master', 16: 'Voyager',
};
const ITEM_TYPES = {
    1: 'Sword', 2: 'Two-handed Sword', 3: 'Bow', 4: 'Firearm', 5: 'Dagger', 6: 'Staff', 7: 'Cannon', 11: 'Shield',
    20: 'Hat', 22: 'Armor', 23: 'Gloves', 24: 'Boots', 25: 'Necklace', 26: 'Ring', 27: 'Tattoo', 44: 'Wings',
    59: 'Pet',
};

const ok = (res, data, status = 200) => res.status(status).json({ success: true, data });
const fail = (res, status, code, message) => res.status(status).json({ success: false, error: { code, message } });

function loadItems() {
    const file = process.env.ITEMINFO_PATH || path.join(__dirname, 'data', 'iteminfo.txt');
    if (!fs.existsSync(file)) return [];
    const out = [];
    for (const line of fs.readFileSync(file, 'latin1').split(/\r?\n/)) {
        if (!line || line.startsWith('//')) continue;
        const c = line.split('\t');
        const id = parseInt(c[0], 10);
        if (!id || c.length < 30 || !c[1]) continue;
        const type = parseInt(c[10], 10);
        out.push({
            id, name: c[1], type: ITEM_TYPES[type] || 'Item', rarity: 'Normal',
            level: parseInt(c[24], 10) || 1, description: (c[c.length - 2] || '').trim(), image: '/placeholder.svg',
        });
    }
    return out;
}

module.exports = function register(app, db) {
    const secret = () => process.env.JWT_SECRET;
    const items = loadItems();
    console.log(`[Portal] ${items.length} itens carregados para o banco de dados publico.`);

    const authLimiter = rateLimit({
        windowMs: 15 * 60 * 1000, max: 20, standardHeaders: true, legacyHeaders: false,
        handler: (req, res) => fail(res, 429, 'RATE_LIMIT', 'Muitas tentativas. Tente novamente em 15 minutos.'),
    });
    const wrap = fn => (req, res) => fn(req, res).catch(e => {
        console.error('[Portal]', req.method, req.path, e.message);
        fail(res, 500, 'SERVER_ERROR', 'Erro interno do servidor.');
    });
    const sec = (accountId, type, desc, ip) =>
        db.execute('INSERT INTO security_logs (account_id, log_type, description, ip_address) VALUES (?, ?, ?, ?)',
            [accountId, type, desc, ip]).catch(() => { });

    async function auth(req, res, next) {
        const h = req.headers.authorization || '';
        const token = h.startsWith('Bearer ') ? h.slice(7) : null;
        if (!token) return fail(res, 401, 'UNAUTHORIZED', 'Login necessario.');
        try {
            const d = jwt.verify(token, secret());
            const [rows] = await db.execute(
                'SELECT id, username, email, is_admin, is_banned, created_at, last_login FROM accounts WHERE id = ? AND session_token = ? AND session_expires > NOW() LIMIT 1',
                [d.sub, token]);
            if (!rows.length || rows[0].is_banned) return fail(res, 401, 'UNAUTHORIZED', 'Sessao invalida.');
            req.user = rows[0];
            next();
        } catch (e) {
            fail(res, 401, 'UNAUTHORIZED', 'Sessao invalida.');
        }
    }

    const v1 = '/api/v1';

    // ---------- Publico ----------
    app.get(`${v1}/server/status`, wrap(async (req, res) => {
        const [[o]] = await db.execute('SELECT COUNT(*) AS n FROM characters WHERE is_online = 1 AND is_deleted = 0');
        ok(res, {
            status: 'online', playersOnline: o.n, onlineRecord: o.n, serverTime: new Date().toISOString(),
            version: process.env.GAME_VERSION || '0.1.0 Alpha', expRate: 'x1', dropRate: 'x1',
        });
    }));

    app.get(`${v1}/news`, wrap(async (req, res) => {
        const [rows] = await db.execute(
            "SELECT id, category, title, excerpt, DATE_FORMAT(created_at, '%Y-%m-%d') AS date FROM news ORDER BY created_at DESC LIMIT 50");
        ok(res, rows);
    }));

    app.get(`${v1}/rankings/players`, wrap(async (req, res) => {
        const [rows] = await db.execute(
            `SELECT name, level, job, exp FROM characters WHERE is_deleted = 0 ORDER BY level DESC, exp DESC LIMIT 100`);
        ok(res, rows.map((r, i) => ({
            rank: i + 1, name: r.name, level: r.level, className: CLASSES[r.job] || 'Novice', guild: '-', power: Number(r.exp),
        })));
    }));

    app.get(`${v1}/database/monsters`, (req, res) => ok(res, []));
    app.get(`${v1}/shop/products`, (req, res) => ok(res, []));
    app.get(`${v1}/database/items`, (req, res) => {
        const q = String(req.query.q || '').toLowerCase().slice(0, 64);
        const list = q ? items.filter(i => i.name.toLowerCase().includes(q)) : items;
        ok(res, list.slice(0, 300));
    });

    // ---------- Conta ----------
    app.post(`${v1}/auth/register`, authLimiter, wrap(async (req, res) => {
        const { username, email, password } = req.body || {};
        const ip = req.ip;
        if (typeof username !== 'string' || typeof email !== 'string' || typeof password !== 'string')
            return fail(res, 400, 'INVALID', 'Dados incompletos.');
        if (!/^[a-zA-Z0-9_]{3,32}$/.test(username)) return fail(res, 400, 'INVALID', 'Usuario invalido (3-32: letras, numeros, _).');
        if (password.length < 8 || password.length > 128) return fail(res, 400, 'INVALID', 'Senha deve ter entre 8 e 128 caracteres.');
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 255) return fail(res, 400, 'INVALID', 'Email invalido.');
        const [ex] = await db.execute('SELECT id FROM accounts WHERE username = ? OR email = ? LIMIT 1', [username, email]);
        if (ex.length) return fail(res, 409, 'EXISTS', 'Usuario ou email ja cadastrado.');
        const hash = await bcrypt.hash(password, parseInt(process.env.BCRYPT_ROUNDS) || 12);
        const [r] = await db.execute(
            'INSERT INTO accounts (username, email, password_hash, is_admin, created_at) VALUES (?, ?, ?, 0, NOW())', [username, email, hash]);
        await sec(r.insertId, 'REGISTER_OK', `Conta criada pelo site: ${username}`, ip);
        ok(res, { accountId: r.insertId }, 201);
    }));

    app.post(`${v1}/auth/login`, authLimiter, wrap(async (req, res) => {
        const id = (req.body || {}).emailOrUsername || (req.body || {}).email || (req.body || {}).username;
        const password = (req.body || {}).password;
        const ip = req.ip;
        if (typeof id !== 'string' || typeof password !== 'string' || !id || !password)
            return fail(res, 400, 'INVALID', 'Usuario e senha obrigatorios.');
        const [rows] = await db.execute(
            'SELECT id, username, email, password_hash, is_banned, ban_until, failed_logins, locked_until FROM accounts WHERE username = ? OR email = ? LIMIT 1', [id, id]);
        const bad = () => fail(res, 401, 'INVALID_CREDENTIALS', 'Usuario ou senha invalidos.');
        if (!rows.length) return bad();
        const u = rows[0];
        if (u.is_banned && (!u.ban_until || new Date(u.ban_until) > new Date())) return fail(res, 403, 'BANNED', 'Conta suspensa.');
        if (u.locked_until && new Date(u.locked_until) > new Date()) return fail(res, 423, 'LOCKED', 'Conta temporariamente bloqueada.');
        if (!(await bcrypt.compare(password, u.password_hash))) {
            const n = (u.failed_logins || 0) + 1;
            await db.execute('UPDATE accounts SET failed_logins = ?, locked_until = ? WHERE id = ?',
                [n, n >= 5 ? new Date(Date.now() + 30 * 60 * 1000) : null, u.id]);
            await sec(u.id, 'LOGIN_FAIL', `Site: senha incorreta (${n})`, ip);
            return bad();
        }
        const token = jwt.sign({ sub: u.id, usr: u.username }, secret(), { expiresIn: process.env.JWT_EXPIRES_IN || '2h' });
        await db.execute(
            `UPDATE accounts SET failed_logins = 0, locked_until = NULL, last_login = NOW(), last_login_ip = ?,
             session_token = ?, session_expires = DATE_ADD(NOW(), INTERVAL 24 HOUR) WHERE id = ?`, [ip, token, u.id]);
        await sec(u.id, 'LOGIN_OK', 'Login pelo site', ip);
        ok(res, { accessToken: token, user: { username: u.username, email: u.email } });
    }));

    app.post(`${v1}/auth/logout`, auth, wrap(async (req, res) => {
        await db.execute('UPDATE accounts SET session_token = NULL WHERE id = ?', [req.user.id]);
        ok(res, {});
    }));

    app.get(`${v1}/account/me`, auth, wrap(async (req, res) => {
        const u = req.user;
        ok(res, { username: u.username, email: u.email, status: 'Active', createdAt: u.created_at, lastLogin: u.last_login });
    }));

    app.get(`${v1}/characters`, auth, wrap(async (req, res) => {
        const [rows] = await db.execute(
            `SELECT id, name, level, job, gold, map_name AS map, play_time_seconds AS playTime
             FROM characters WHERE account_id = ? AND is_deleted = 0 ORDER BY slot_index`, [req.user.id]);
        ok(res, rows.map(r => ({ ...r, className: CLASSES[r.job] || 'Novice', gold: Number(r.gold), playTime: Number(r.playTime) })));
    }));

    app.get(`${v1}/characters/:id/inventory`, auth, wrap(async (req, res) => {
        const id = parseInt(req.params.id, 10);
        const [own] = await db.execute('SELECT id FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0', [id, req.user.id]);
        if (!own.length) return fail(res, 404, 'NOT_FOUND', 'Personagem nao encontrado.');
        const [rows] = await db.execute(
            'SELECT slot_index AS slot, item_id AS itemId, quantity FROM inventory WHERE character_id = ? ORDER BY slot_index', [id]);
        ok(res, rows);
    }));

    app.use(`${v1}`, (req, res) => fail(res, 404, 'NOT_FOUND', 'Rota nao encontrada.'));
};
