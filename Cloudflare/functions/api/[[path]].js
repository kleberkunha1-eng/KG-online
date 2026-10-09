// API do jogo e do site (Cloudflare Pages Functions + D1). Mesmas rotas da API Node antiga.
import { Hono } from 'hono';
import { handle } from 'hono/cloudflare-pages';
import ITEMS from '../_items.json';

const app = new Hono().basePath('/api');

const CLASSES = {
    0: 'Novice', 1: 'Swordsman', 2: 'Hunter', 4: 'Explorer', 5: 'Herbalist', 8: 'Champion', 9: 'Crusader',
    10: 'Crusader', 11: 'Sharpshooter', 12: 'Sharpshooter', 13: 'Cleric', 14: 'Seal Master', 16: 'Voyager',
};
const BASE_STATS = { 0: [15, 10, 12, 8, 10, 150, 50, 100], 1: [10, 15, 10, 10, 12, 120, 60, 100], 2: [8, 10, 10, 15, 10, 100, 80, 100], 3: [10, 12, 10, 12, 11, 110, 70, 100] };
BASE_STATS[4] = BASE_STATS[0]; // NewCharacterTest inherits Lance's starting attributes.
const DEFAULT_STATS = [10, 10, 10, 10, 10, 100, 50, 100];
const MAX_INV = 300, MAX_SKILLS = 300;
const SESSION_SECONDS = 24 * 3600;

const now = () => Math.floor(Date.now() / 1000);
const int = (v, d = 0) => { const n = parseInt(v, 10); return Number.isFinite(n) ? n : d; };
const num = (v, d = 0) => { const n = Number(v); return Number.isFinite(n) ? n : d; };
const opt = v => (v === undefined || v === null || v < 0 ? null : v);
const ipOf = c => c.req.header('CF-Connecting-IP') || '0.0.0.0';

// ---------- criptografia (WebCrypto) ----------
const enc = new TextEncoder();
const b64 = buf => btoa(String.fromCharCode(...new Uint8Array(buf)));
const unb64 = s => Uint8Array.from(atob(s), ch => ch.charCodeAt(0));
const b64url = buf => b64(buf).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const unb64url = s => unb64(s.replace(/-/g, '+').replace(/_/g, '/'));
const ITER = 100000; // maximo aceito pelo Workers

async function pbkdf2(password, salt, iter) {
    const key = await crypto.subtle.importKey('raw', enc.encode(password), 'PBKDF2', false, ['deriveBits']);
    return crypto.subtle.deriveBits({ name: 'PBKDF2', hash: 'SHA-256', salt, iterations: iter }, key, 256);
}
async function hashPassword(password) {
    const salt = crypto.getRandomValues(new Uint8Array(16));
    return `pbkdf2$${ITER}$${b64(salt)}$${b64(await pbkdf2(password, salt, ITER))}`;
}
async function verifyPassword(password, stored) {
    const [kind, iter, salt, hash] = String(stored).split('$');
    if (kind !== 'pbkdf2') return false;
    const got = new Uint8Array(await pbkdf2(password, unb64(salt), int(iter)));
    const want = unb64(hash);
    if (got.length !== want.length) return false;
    let d = 0;
    for (let i = 0; i < got.length; i++) d |= got[i] ^ want[i];
    return d === 0;
}
async function hmacKey(secret, usage) {
    return crypto.subtle.importKey('raw', enc.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, [usage]);
}
async function signJwt(payload, secret, ttl) {
    const body = { ...payload, iat: now(), exp: now() + ttl };
    const head = b64url(enc.encode(JSON.stringify({ alg: 'HS256', typ: 'JWT' })));
    const data = head + '.' + b64url(enc.encode(JSON.stringify(body)));
    const sig = await crypto.subtle.sign('HMAC', await hmacKey(secret, 'sign'), enc.encode(data));
    return data + '.' + b64url(sig);
}
async function verifyJwt(token, secret) {
    const p = String(token).split('.');
    if (p.length !== 3) return null;
    const ok = await crypto.subtle.verify('HMAC', await hmacKey(secret, 'verify'), unb64url(p[2]), enc.encode(p[0] + '.' + p[1]));
    if (!ok) return null;
    const body = JSON.parse(new TextDecoder().decode(unb64url(p[1])));
    return body.exp > now() ? body : null;
}

// ---------- helpers ----------
const one = (c, sql, ...p) => c.env.DB.prepare(sql).bind(...p).first();
const all = async (c, sql, ...p) => (await c.env.DB.prepare(sql).bind(...p).all()).results;
const run = (c, sql, ...p) => c.env.DB.prepare(sql).bind(...p).run();
const sec = (c, accountId, type, desc) =>
    run(c, 'INSERT INTO security_logs (account_id, log_type, description, ip_address) VALUES (?, ?, ?, ?)', accountId, type, desc, ipOf(c)).catch(() => { });
const body = async c => { try { const b = await c.req.json(); return b && typeof b === 'object' ? b : {}; } catch { return {}; } };

// limite por IP: 20 tentativas de login/registro em 15 minutos
async function throttled(c) {
    const r = await one(c, "SELECT COUNT(*) AS n FROM security_logs WHERE ip_address = ? AND log_type IN ('LOGIN_FAIL','LOGIN_OK','REGISTER_OK') AND created_at > ?", ipOf(c), now() - 900);
    return r.n >= 20;
}

app.onError((e, c) => {
    console.error('[API]', c.req.method, c.req.path, e && e.message);
    return c.json({ success: false, error: 'Erro interno do servidor.' }, 500);
});

app.get('/health', c => c.json({ status: 'API Online', timestamp: new Date().toISOString() }));

// ---------- autenticacao ----------
async function sessionUser(c) {
    const h = c.req.header('Authorization') || '';
    const token = h.startsWith('Bearer ') ? h.slice(7) : null;
    if (!token) return null;
    const d = await verifyJwt(token, c.env.JWT_SECRET);
    if (!d) return null;
    const u = await one(c, 'SELECT id, username, email, is_admin, is_banned, created_at, last_login FROM accounts WHERE id = ? AND session_token = ? AND session_expires > ?', d.sub, token, now());
    return u && !u.is_banned ? u : null;
}

async function createAccount(c, b, minPass, admin = 0) {
    const { username, email, password } = b;
    if (typeof username !== 'string' || typeof email !== 'string' || typeof password !== 'string') return { status: 400, error: 'Dados incompletos.' };
    if (!/^[a-zA-Z0-9_]{3,32}$/.test(username)) return { status: 400, error: 'Usuario invalido (3-32: letras, numeros, _).' };
    if (password.length < minPass || password.length > 128) return { status: 400, error: `Senha deve ter entre ${minPass} e 128 caracteres.` };
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 255) return { status: 400, error: 'Email invalido.' };
    if (await throttled(c)) return { status: 429, error: 'Muitas tentativas. Tente novamente em 15 minutos.' };
    if (await one(c, 'SELECT id FROM accounts WHERE username = ? OR email = ?', username, email)) return { status: 409, error: 'Usuario ou email ja cadastrado.' };
    const r = await run(c, 'INSERT INTO accounts (username, email, password_hash, is_admin) VALUES (?, ?, ?, ?)', username, email, await hashPassword(password), admin);
    const id = r.meta.last_row_id;
    await sec(c, id, 'REGISTER_OK', 'Conta criada: ' + username);
    return { id };
}

async function login(c, ident, password) {
    if (typeof ident !== 'string' || typeof password !== 'string' || !ident || !password) return { status: 400, error: 'Usuario e senha obrigatorios.' };
    if (await throttled(c)) return { status: 429, error: 'Muitas tentativas. Tente novamente em 15 minutos.' };
    const u = await one(c, 'SELECT id, username, email, password_hash, is_admin, is_banned, ban_until, failed_logins, locked_until FROM accounts WHERE username = ? OR email = ?', ident, ident);
    if (!u) { await sec(c, null, 'LOGIN_FAIL', 'Usuario nao encontrado'); return { status: 401, error: 'Usuario ou senha invalidos.' }; }
    if (u.is_banned && (!u.ban_until || u.ban_until > now())) return { status: 403, error: 'Conta suspensa.' };
    if (u.locked_until && u.locked_until > now()) return { status: 423, error: 'Conta temporariamente bloqueada por tentativas excessivas.' };
    if (!(await verifyPassword(password, u.password_hash))) {
        const n = (u.failed_logins || 0) + 1;
        await run(c, 'UPDATE accounts SET failed_logins = ?, locked_until = ? WHERE id = ?', n, n >= 5 ? now() + 1800 : null, u.id);
        await sec(c, u.id, 'LOGIN_FAIL', `Senha incorreta (${n})`);
        return { status: 401, error: 'Usuario ou senha invalidos.' };
    }
    const token = await signJwt({ sub: u.id, usr: u.username }, c.env.JWT_SECRET, SESSION_SECONDS);
    await run(c, 'UPDATE accounts SET failed_logins = 0, locked_until = NULL, last_login = ?, last_login_ip = ?, session_token = ?, session_expires = ? WHERE id = ?',
        now(), ipOf(c), token, now() + SESSION_SECONDS, u.id);
    await sec(c, u.id, 'LOGIN_OK', 'Login');
    return { u, token };
}

// Cliente Unity
app.post('/auth/register', async c => {
    const r = await createAccount(c, await body(c), 6);
    if (r.error) return c.json({ success: false, error: r.error }, r.status);
    return c.json({ success: true, message: 'Conta criada com sucesso!', accountId: r.id }, 201);
});
app.post('/auth/login', async c => {
    const b = await body(c);
    const r = await login(c, b.username, b.password);
    if (r.error) return c.json({ success: false, error: r.error }, r.status);
    return c.json({ success: true, token: r.token, accountId: r.u.id, username: r.u.username, isAdmin: !!r.u.is_admin });
});
app.post('/auth/verify', async c => {
    const { token } = await body(c);
    if (!token) return c.json({ valid: false }, 400);
    const d = await verifyJwt(token, c.env.JWT_SECRET).catch(() => null);
    if (!d) return c.json({ valid: false });
    const u = await one(c, 'SELECT id, is_banned FROM accounts WHERE id = ? AND session_token = ? AND session_expires > ?', d.sub, token, now());
    if (!u || u.is_banned) return c.json({ valid: false });
    return c.json({ valid: true, accountId: d.sub, username: d.usr });
});

// ---------- relatorios de erro do launcher/jogo (sem autenticacao: o jogo crashou antes do login) ----------
app.post('/crash-report', async c => {
    const ip = ipOf(c);
    const recent = await one(c, 'SELECT COUNT(*) AS n FROM crash_reports WHERE ip_address = ? AND created_at > ?', ip, now() - 3600);
    if (recent.n >= 10) return c.json({ success: false, error: 'Muitos relatorios enviados. Tente novamente mais tarde.' }, 429);
    const b = await body(c);
    const log = typeof b.log === 'string' ? b.log.slice(0, 200000) : '';
    await run(c, 'INSERT INTO crash_reports (launcher_version, game_version, exit_code, exit_signal, os_info, log_text, ip_address) VALUES (?, ?, ?, ?, ?, ?, ?)',
        String(b.launcherVersion || '').slice(0, 32), String(b.gameVersion || '').slice(0, 32),
        Number.isFinite(b.exitCode) ? int(b.exitCode) : null, String(b.exitSignal || '').slice(0, 32),
        String(b.osInfo || '').slice(0, 500), log, ip);
    return c.json({ success: true });
});

// ---------- site (/api/v1) ----------
const ok = (c, data, status = 200) => c.json({ success: true, data }, status);
const fail = (c, status, code, message) => c.json({ success: false, error: { code, message } }, status);
const v1 = new Hono();

v1.get('/server/status', async c => {
    const o = await one(c, 'SELECT COUNT(*) AS n FROM characters WHERE is_online = 1 AND is_deleted = 0');
    return ok(c, { status: 'online', playersOnline: o.n, onlineRecord: o.n, serverTime: new Date().toISOString(), version: c.env.GAME_VERSION || '0.1.0 Alpha', expRate: 'x1', dropRate: 'x1' });
});
v1.get('/news', async c => {
    const rows = await all(c, "SELECT id, category, title, excerpt, date(created_at, 'unixepoch') AS date FROM news ORDER BY created_at DESC LIMIT 50");
    return ok(c, rows);
});
v1.get('/rankings/players', async c => {
    const rows = await all(c, 'SELECT name, level, job, exp FROM characters WHERE is_deleted = 0 ORDER BY level DESC, exp DESC LIMIT 100');
    return ok(c, rows.map((r, i) => ({ rank: i + 1, name: r.name, level: r.level, className: CLASSES[r.job] || 'Novice', guild: '-', power: r.exp })));
});
v1.get('/database/monsters', c => ok(c, []));
v1.get('/shop/products', c => ok(c, []));
v1.get('/database/items', c => {
    const q = String(c.req.query('q') || '').toLowerCase().slice(0, 64);
    const list = q ? ITEMS.filter(i => i.name.toLowerCase().includes(q)) : ITEMS;
    return ok(c, list.slice(0, 300));
});
v1.post('/auth/register', async c => {
    const r = await createAccount(c, await body(c), 8);
    if (r.error) return fail(c, r.status, r.status === 409 ? 'EXISTS' : r.status === 429 ? 'RATE_LIMIT' : 'INVALID', r.error);
    return ok(c, { accountId: r.id }, 201);
});
v1.post('/auth/login', async c => {
    const b = await body(c);
    const r = await login(c, b.emailOrUsername || b.email || b.username, b.password);
    if (r.error) return fail(c, r.status, r.status === 401 ? 'INVALID_CREDENTIALS' : r.status === 429 ? 'RATE_LIMIT' : 'ERROR', r.error);
    return ok(c, { accessToken: r.token, user: { username: r.u.username, email: r.u.email } });
});
v1.post('/auth/logout', async c => {
    const u = await sessionUser(c);
    if (!u) return fail(c, 401, 'UNAUTHORIZED', 'Login necessario.');
    await run(c, 'UPDATE accounts SET session_token = NULL WHERE id = ?', u.id);
    return ok(c, {});
});
v1.get('/account/me', async c => {
    const u = await sessionUser(c);
    if (!u) return fail(c, 401, 'UNAUTHORIZED', 'Login necessario.');
    return ok(c, { username: u.username, email: u.email, status: 'Active', createdAt: new Date(u.created_at * 1000).toISOString(), lastLogin: u.last_login ? new Date(u.last_login * 1000).toISOString() : null });
});
v1.get('/characters', async c => {
    const u = await sessionUser(c);
    if (!u) return fail(c, 401, 'UNAUTHORIZED', 'Login necessario.');
    const rows = await all(c, 'SELECT id, name, level, job, gold, map_name AS map, play_time_seconds AS playTime FROM characters WHERE account_id = ? AND is_deleted = 0 ORDER BY slot_index', u.id);
    return ok(c, rows.map(r => ({ ...r, className: CLASSES[r.job] || 'Novice' })));
});
v1.get('/characters/:id/inventory', async c => {
    const u = await sessionUser(c);
    if (!u) return fail(c, 401, 'UNAUTHORIZED', 'Login necessario.');
    const id = int(c.req.param('id'));
    if (!(await one(c, 'SELECT id FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0', id, u.id))) return fail(c, 404, 'NOT_FOUND', 'Personagem nao encontrado.');
    return ok(c, await all(c, 'SELECT slot_index AS slot, item_id AS itemId, quantity FROM inventory WHERE character_id = ? ORDER BY slot_index', id));
});
v1.all('*', c => fail(c, 404, 'NOT_FOUND', 'Rota nao encontrada.'));
app.route('/v1', v1);

// ---------- jogo (/api/game) ----------
const game = new Hono();
game.use('*', async (c, next) => {
    const u = await sessionUser(c);
    if (!u) return c.json({ success: false, error: 'UNAUTHORIZED' }, 401);
    c.set('user', u);
    await next();
});
const owned = async (c, charId, accountId) => !!(await one(c, 'SELECT id FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0', charId, accountId));

game.get('/me', c => { const u = c.get('user'); return c.json({ success: true, admin: !!u.is_admin, username: u.username, accountId: u.id }); });

game.get('/characters', async c => {
    const u = c.get('user');
    const rows = await all(c,
        `SELECT id, slot_index, name, gender, job, level, map_name, pos_x, pos_y, pos_z, rotation_y, hair_style, hair_color, face_style, last_online,
                (SELECT GROUP_CONCAT(item_id) FROM inventory i WHERE i.character_id = characters.id AND i.is_equipped = 1) AS equipped
         FROM characters WHERE account_id = ? AND is_deleted = 0 ORDER BY slot_index`, u.id);
    return c.json({
        success: true,
        Characters: rows.map(r => ({
            Id: r.id, SlotIndex: r.slot_index, Name: r.name, Gender: r.gender, Job: r.job, Level: r.level, MapName: r.map_name || 'garner',
            PosX: r.pos_x, PosY: r.pos_y, PosZ: r.pos_z, RotationY: r.rotation_y, HairStyle: r.hair_style, HairColor: r.hair_color, FaceStyle: r.face_style,
            Equipped: r.equipped || '', LastOnline: r.last_online || 0,
        })),
    });
});

game.post('/characters', async c => {
    const u = c.get('user'), b = await body(c);
    const name = typeof b.name === 'string' ? b.name.trim() : '';
    if (name.length < 3 || name.length > 16 || /[\u0000-\u001f<>]/.test(name)) return c.json({ success: false, error: 'NAME_INVALID' });
    const slot = int(b.slot), job = int(b.job), gender = int(b.gender);
    if (slot < 0 || slot > 5) return c.json({ success: false, error: 'SLOT_INVALID' });
    if (await one(c, 'SELECT id FROM characters WHERE account_id = ? AND slot_index = ? AND is_deleted = 0', u.id, slot)) return c.json({ success: false, error: 'SLOT_OCCUPIED' });
    if (await one(c, 'SELECT id FROM characters WHERE name = ? AND is_deleted = 0', name)) return c.json({ success: false, error: 'NAME_TAKEN' });
    const [str, agi, con, spr, sta, hp, mp, sp] = BASE_STATS[job] || DEFAULT_STATS;
    const city = b.city || {};
    try {
        const r = await run(c,
            `INSERT INTO characters (account_id, slot_index, name, gender, job, base_str, base_agi, base_con, base_spr, base_sta, max_hp, max_mp, max_sp,
                current_hp, current_mp, current_sp, map_name, pos_x, pos_y, pos_z, rotation_y, hair_style, hair_color, face_style)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            u.id, slot, name, gender, job, str, agi, con, spr, sta, hp, mp, sp, hp, mp, sp,
            String(city.map || 'garner').slice(0, 64), num(city.x), num(city.y, 1.5), num(city.z), num(city.rotY), int(b.hairStyle), int(b.hairColor), int(b.faceStyle));
        return c.json({ success: true, charId: r.meta.last_row_id });
    } catch (e) { return c.json({ success: false, error: 'NAME_TAKEN' }); }
});

game.get('/characters/:id', async c => {
    const u = c.get('user'), id = int(c.req.param('id'));
    const r = await one(c, 'SELECT * FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0', id, u.id);
    if (!r) return c.json({ success: false, error: 'NOT_FOUND' });
    const inv = await all(c, 'SELECT * FROM inventory WHERE character_id = ?', id);
    const sk = await all(c, 'SELECT id, skill_id, level, exp FROM skills WHERE character_id = ?', id);
    return c.json({
        success: true,
        Character: {
            Id: r.id, AccountId: r.account_id, Name: r.name, Job: r.job, Gender: r.gender, Level: r.level, Exp: r.exp,
            SaveRevision: r.save_revision || 0,
            Boats: JSON.parse(r.boats_json || '[]'),
            BoatOwnershipVersion: r.boats_json !== undefined ? 1 : 0,
            CurrentHp: r.current_hp, CurrentMp: r.current_mp, CurrentSp: r.current_sp, MaxHp: r.max_hp, MaxMp: r.max_mp, MaxSp: r.max_sp,
            BaseStr: r.base_str, BaseAgi: r.base_agi, BaseCon: r.base_con, BaseSpr: r.base_spr, BaseSta: r.base_sta,
            Gold: r.gold, StatPoints: r.stat_points, SkillPoints: r.skill_points, PkPoints: r.pk_points, Reputation: r.reputation,
            HairStyle: r.hair_style, HairColor: r.hair_color, FaceStyle: r.face_style,
            MapName: r.map_name || 'garner', PosX: r.pos_x, PosY: r.pos_y, PosZ: r.pos_z, RotationY: r.rotation_y,
        },
        Inventory: inv.map(i => ({
            Id: i.id, UniqueItemId: i.unique_item_id, SlotIndex: i.slot_index, ItemId: i.item_id, Quantity: i.quantity, Durability: i.durability ?? 100,
            IsEquipped: !!i.is_equipped, IsLocked: !!i.is_locked, OwnerCharacterId: i.owner_character_id ?? -1, RefineLevel: i.refine_level || 0,
            Gem1: i.gem_slot_1 ?? -1, Gem2: i.gem_slot_2 ?? -1, Gem3: i.gem_slot_3 ?? -1,
        })),
        Skills: sk.map(s => ({ Id: s.id, SkillId: s.skill_id, Level: s.level, Exp: s.exp })),
    });
});

// Salva so o que mudou (poupa as escritas do plano gratuito do D1).
const itemRow = (id, i) => [
    String(i.UniqueItemId || crypto.randomUUID()).slice(0, 64), id, int(i.SlotIndex), int(i.ItemId), Math.max(1, int(i.Quantity, 1)), int(i.Durability, 100),
    int(i.RefineLevel), opt(i.Gem1), opt(i.Gem2), opt(i.Gem3), i.IsEquipped ? 1 : 0, i.IsLocked ? 1 : 0, opt(i.OwnerCharacterId),
];
const COLS = ['unique_item_id', 'character_id', 'slot_index', 'item_id', 'quantity', 'durability', 'refine_level', 'gem_slot_1', 'gem_slot_2', 'gem_slot_3', 'is_equipped', 'is_locked', 'owner_character_id'];

game.put('/characters/:id', async c => {
    const u = c.get('user'), id = Number(c.req.param('id')), b = await body(c);
    if (!Number.isSafeInteger(id) || id <= 0) return c.json({ success: false, error: 'INVALID_CHARACTER' });
    if (!(await owned(c, id, u.id))) return c.json({ success: false, error: 'NOT_FOUND' });
    const ch = b.Character || {}, inv = b.Inventory, skills = b.Skills;
    const questId = b.QuestId === undefined ? 0 : b.QuestId;
    if (typeof b.OperationId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(b.OperationId)
        || !Number.isSafeInteger(ch.SaveRevision) || ch.SaveRevision < 0 || ch.SaveRevision >= Number.MAX_SAFE_INTEGER
        || !Number.isInteger(questId) || questId < 0 || questId > 2147483647)
        return c.json({ success: false, error: 'INVALID_TRANSACTION' });
    if ((inv && (!Array.isArray(inv) || inv.length > MAX_INV)) || (skills && (!Array.isArray(skills) || skills.length > MAX_SKILLS))) return c.json({ success: false, error: 'INVALID' });
    if (questId > 0 && (!Array.isArray(inv) || !Array.isArray(skills)))
        return c.json({ success: false, error: 'INVALID_TRANSACTION' });
    if (!Number.isSafeInteger(ch.Gold) || ch.Gold < 0 || !Number.isSafeInteger(ch.Exp) || ch.Exp < 0)
        return c.json({ success: false, error: 'INVALID_CHARACTER_VALUES' });
    const boats = ch.Boats;
    if (boats !== undefined && (!Array.isArray(boats) || boats.length > 3
        || new Set(boats.map(boat => boat && boat.Id)).size !== boats.length
        || boats.some(boat => !boat || typeof boat.Id !== 'string'
            || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(boat.Id)
            || typeof boat.Name !== 'string' || !/^[\x20-\x7e]{2,16}$/.test(boat.Name) || /[<>]/.test(boat.Name)
            || ['TypeId','BerthId','Level','HullId','EngineId','BowId','CannonId','ComponentId','Health','Fuel']
                .some(key => !Number.isInteger(boat[key]) || boat[key] < 0 || boat[key] > 2147483647)
            || boat.TypeId < 1 || boat.BerthId < 1 || boat.Level < 1)))
        return c.json({ success: false, error: 'INVALID_BOATS' });
    const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', enc.encode(JSON.stringify(b)))))
        .map(value => value.toString(16).padStart(2, '0')).join('');
    const receipt = await one(c, 'SELECT payload_hash, expected_revision FROM character_save_receipts WHERE character_id = ? AND operation_id = ?', id, b.OperationId);
    const replay = saved => saved.payload_hash === hash
        ? c.json({ success: true, revision: saved.expected_revision + 1, boatOwnershipVersion: 1 })
        : c.json({ success: false, error: 'OPERATION_MISMATCH' });
    if (receipt) return replay(receipt);
    const db = c.env.DB, stmts = [];
    stmts.push(db.prepare('INSERT INTO character_save_receipts (character_id, operation_id, payload_hash, expected_revision, quest_id) VALUES (?, ?, ?, ?, ?)')
        .bind(id, b.OperationId, hash, ch.SaveRevision, questId));
    if (questId > 0)
        stmts.push(db.prepare('UPDATE quest_progress SET completed_at = ? WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL')
            .bind(now(), id, questId));
    stmts.push(db.prepare(
        `UPDATE characters SET save_revision=save_revision+1, level=?, exp=?, current_hp=?, current_mp=?, current_sp=?, pos_x=?, pos_y=?, pos_z=?, rotation_y=?, map_name=?,
            base_str=?, base_agi=?, base_con=?, base_spr=?, base_sta=?, max_hp=?, max_mp=?, max_sp=?,
            gold=?, stat_points=?, skill_points=?, pk_points=?, reputation=?, last_online=? WHERE id = ? AND account_id = ?`).bind(
        Math.max(1, int(ch.Level, 1)), Math.max(0, num(ch.Exp)), int(ch.CurrentHp), int(ch.CurrentMp), int(ch.CurrentSp),
        num(ch.PosX), num(ch.PosY), num(ch.PosZ), num(ch.RotationY), String(ch.MapName || 'garner').slice(0, 64),
        int(ch.BaseStr), int(ch.BaseAgi), int(ch.BaseCon), int(ch.BaseSpr), int(ch.BaseSta), int(ch.MaxHp), int(ch.MaxMp), int(ch.MaxSp),
        Math.max(0, num(ch.Gold)), int(ch.StatPoints), int(ch.SkillPoints), int(ch.PkPoints), int(ch.Reputation), now(), id, u.id));
    if (boats !== undefined)
        stmts.push(db.prepare('UPDATE characters SET boats_json = ? WHERE id = ?').bind(JSON.stringify(boats), id));

    if (inv) {
        const cur = new Map((await all(c, 'SELECT * FROM inventory WHERE character_id = ?', id)).map(r => [r.unique_item_id, r]));
        const incoming = inv.map(i => itemRow(id, i));
        const keep = new Set(), toInsert = [], toDelete = [];
        for (const row of incoming) {
            const e = cur.get(row[0]);
            const same = e && COLS.every((col, k) => (e[col] ?? null) === row[k]);
            keep.add(row[0]);
            if (same) continue;
            if (e) toDelete.push(row[0]);
            toInsert.push(row);
        }
        for (const uid of cur.keys()) if (!keep.has(uid)) toDelete.push(uid);
        for (const uid of toDelete) stmts.push(db.prepare('DELETE FROM inventory WHERE unique_item_id = ? AND character_id = ?').bind(uid, id));
        for (const row of toInsert)
            stmts.push(db.prepare(`INSERT INTO inventory (${COLS.join(',')}) VALUES (${COLS.map(() => '?').join(',')})`).bind(...row));
    }
    if (skills) {
        const cur = new Map((await all(c, 'SELECT skill_id, level, exp FROM skills WHERE character_id = ?', id)).map(r => [r.skill_id, r]));
        const seen = new Set();
        for (const s of skills) {
            const sid = int(s.SkillId), lvl = int(s.Level), exp = Math.max(0, num(s.Exp));
            seen.add(sid);
            const e = cur.get(sid);
            if (e && e.level === lvl && e.exp === exp) continue;
            stmts.push(db.prepare('INSERT INTO skills (character_id, skill_id, level, exp) VALUES (?, ?, ?, ?) ON CONFLICT(character_id, skill_id) DO UPDATE SET level = excluded.level, exp = excluded.exp').bind(id, sid, lvl, exp));
        }
        for (const sid of cur.keys()) if (!seen.has(sid)) stmts.push(db.prepare('DELETE FROM skills WHERE character_id = ? AND skill_id = ?').bind(id, sid));
    }
    try {
        await db.batch(stmts);
    } catch (error) {
        const saved = await one(c, 'SELECT payload_hash, expected_revision FROM character_save_receipts WHERE character_id = ? AND operation_id = ?', id, b.OperationId);
        if (saved) return replay(saved);
        if (String(error.message).includes('SAVE_CONFLICT')) return c.json({ success: false, error: 'SAVE_CONFLICT' });
        if (String(error.message).includes('QUEST_NOT_ACTIVE')) return c.json({ success: false, error: 'QUEST_NOT_ACTIVE' });
        throw error;
    }
    return c.json({ success: true, revision: ch.SaveRevision + 1, boatOwnershipVersion: 1 });
});

game.post('/characters/:id/delete', async c => {
    const u = c.get('user'), id = int(c.req.param('id')), { password } = await body(c);
    if (typeof password !== 'string' || !password) return c.json({ success: false, error: 'INVALID_PASSWORD' });
    if (!(await owned(c, id, u.id))) return c.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
    const acc = await one(c, 'SELECT password_hash FROM accounts WHERE id = ?', u.id);
    if (!(await verifyPassword(password, acc.password_hash))) return c.json({ success: false, error: 'INVALID_PASSWORD' });
    await run(c, "UPDATE characters SET is_deleted = 1, deleted_at = ?, name = name || '_deleted_' || ? WHERE id = ? AND account_id = ?", now(), now(), id, u.id);
    return c.json({ success: true });
});

const questNumber = (value, minimum = 1) =>
    Number.isInteger(value) && value >= minimum && value <= 2147483647;

game.get('/quests/:charId', async c => {
    const charId = Number(c.req.param('charId'));
    if (!Number.isSafeInteger(charId) || charId <= 0) return c.json({ success: false, error: 'INVALID_CHARACTER' });
    if (!(await owned(c, charId, c.get('user').id))) return c.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
    const rows = await all(c, 'SELECT quest_id, progress, completed_at FROM quest_progress WHERE character_id = ?', charId);
    const objectives = await all(c, 'SELECT quest_id, objective_index, progress FROM quest_objective_progress WHERE character_id = ? ORDER BY objective_index', charId);
    return c.json({
        success: true,
        active: rows.filter(r => r.completed_at === null).map(r => {
            const extra = objectives.filter(o => o.quest_id === r.quest_id)
                .map(o => ({ objectiveIndex: o.objective_index, progress: o.progress }));
            return { questId: r.quest_id, progress: r.progress, ...(extra.length ? { objectiveProgress: extra } : {}) };
        }),
        completed: rows.filter(r => r.completed_at !== null).map(r => r.quest_id),
    });
});

for (const action of ['accept', 'abandon', 'progress', 'complete']) {
    game.post('/quests/:charId/' + action, async c => {
        const charId = Number(c.req.param('charId')), b = await body(c);
        if (!Number.isSafeInteger(charId) || charId <= 0) return c.json({ success: false, error: 'INVALID_CHARACTER' });
        if (!(await owned(c, charId, c.get('user').id))) return c.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        if (!questNumber(b.questId) || (action === 'progress' && !questNumber(b.progress, 0)))
            return c.json({ success: false, error: 'INVALID_QUEST' });
        const index = b.objectiveIndex === undefined ? 0 : b.objectiveIndex;
        if (action === 'progress' && (!questNumber(index, 0) || index > 15))
            return c.json({ success: false, error: 'INVALID_OBJECTIVE' });
        let result;
        if (action === 'accept') {
            result = await run(c, 'INSERT INTO quest_progress (character_id, quest_id) VALUES (?, ?) ON CONFLICT(character_id, quest_id) DO NOTHING',
                charId, b.questId);
        } else if (action === 'abandon') {
            result = await run(c, 'DELETE FROM quest_progress WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL',
                charId, b.questId);
        } else if (action === 'progress' && index > 0) {
            result = await run(c, `INSERT INTO quest_objective_progress (character_id, quest_id, objective_index, progress)
                SELECT character_id, quest_id, ?, ? FROM quest_progress
                WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL
                ON CONFLICT(character_id, quest_id, objective_index) DO UPDATE SET progress = MAX(progress, excluded.progress)`,
                index, b.progress, charId, b.questId);
        } else if (action === 'progress') {
            result = await run(c, 'UPDATE quest_progress SET progress = MAX(progress, ?) WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL',
                b.progress, charId, b.questId);
        } else {
            result = await run(c, 'UPDATE quest_progress SET completed_at = ? WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL',
                now(), charId, b.questId);
        }
        return result.meta.changes > 0 ? c.json({ success: true })
            : c.json({ success: false, error: action === 'accept' ? 'ALREADY_HAVE_QUEST' : 'QUEST_NOT_ACTIVE' });
    });
}

game.post('/audit', async c => {
    const u = c.get('user'), b = await body(c);
    let data = typeof b.detail === 'string' ? b.detail.slice(0, 4000) : '{}';
    try { JSON.parse(data); } catch { data = JSON.stringify({ text: data }); }
    await run(c, 'INSERT INTO audit_log (account_id, character_id, action_type, action_data, ip_address) VALUES (?, ?, ?, ?, ?)',
        u.id, b.charId > 0 ? int(b.charId) : null, String(b.action || '').slice(0, 64), data, ipOf(c));
    return c.json({ success: true });
});
app.route('/game', game);

// ---------- admin (painel F10 / ApiClient) ----------
const adm = new Hono();
adm.use('*', async (c, next) => {
    const u = await sessionUser(c);
    if (!u) return c.json({ success: false, error: 'Sessao invalida.' }, 401);
    c.set('user', u);
    await next();
});
adm.get('/characters', async c => c.json({ success: true, characters: await all(c, 'SELECT id, slot_index AS slot, name, gender, job, level, map_name AS map, gold FROM characters WHERE account_id = ? AND is_deleted = 0 ORDER BY slot_index', c.get('user').id) }));
adm.get('/characters/:id/inventory', async c => {
    const id = int(c.req.param('id'));
    if (!(await owned(c, id, c.get('user').id))) return c.json({ success: false, error: 'Personagem nao encontrado.' }, 404);
    return c.json({ success: true, items: await all(c, 'SELECT slot_index AS slot, item_id AS itemId, quantity, durability, refine_level AS refine, is_equipped AS equipped FROM inventory WHERE character_id = ? ORDER BY slot_index', id) });
});
adm.get('/admin/me', c => c.json({ success: true, admin: !!c.get('user').is_admin, username: c.get('user').username }));
adm.get('/admin/crash-reports', async c => {
    if (!c.get('user').is_admin) return c.json({ success: false, error: 'Acesso negado.' }, 403);
    const limit = Math.max(1, Math.min(100, int(c.req.query('limit'), 30)));
    const reports = await all(c, 'SELECT id, launcher_version, game_version, exit_code, exit_signal, os_info, log_text, ip_address, created_at FROM crash_reports ORDER BY created_at DESC LIMIT ?', limit);
    return c.json({ success: true, reports });
});
adm.post('/admin/give', async c => {
    if (!c.get('user').is_admin) return c.json({ success: false, error: 'Acesso negado.' }, 403);
    const b = await body(c);
    const characterId = int(b.characterId), itemId = int(b.itemId);
    const qty = Math.max(1, Math.min(9999, int(b.quantity, 1)));
    const refine = Math.max(0, Math.min(12, int(b.refine)));
    const sockets = Math.max(0, Math.min(3, int(b.sockets)));
    const gems = [0, 1, 2].map(i => (i < sockets ? Math.max(0, int((b.gems || [])[i])) : null));
    if (!(await one(c, 'SELECT id FROM characters WHERE id = ? AND is_deleted = 0', characterId))) return c.json({ success: false, error: 'Personagem nao encontrado.' }, 404);
    const taken = new Set((await all(c, 'SELECT slot_index FROM inventory WHERE character_id = ?', characterId)).map(r => r.slot_index));
    let slot = -1;
    for (let i = 0; i < 40; i++) if (!taken.has(i)) { slot = i; break; }
    if (slot < 0) return c.json({ success: false, error: 'Inventario cheio.' }, 409);
    await run(c, 'INSERT INTO inventory (unique_item_id, character_id, slot_index, item_id, quantity, durability, refine_level, is_equipped, gem_slot_1, gem_slot_2, gem_slot_3) VALUES (?, ?, ?, ?, ?, 100, ?, 0, ?, ?, ?)',
        crypto.randomUUID(), characterId, slot, itemId, qty, refine, gems[0], gems[1], gems[2]);
    return c.json({ success: true, slot });
});
app.route('/', adm);

export const onRequest = handle(app);
