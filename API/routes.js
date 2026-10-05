const jwt = require('jsonwebtoken');
const SECRET = () => process.env.JWT_SECRET || 'sua_chave_ultra_secreta_aqui_minimo_32_caracteres_xyz';

module.exports = function register(app, db) {
    async function auth(req, res, next) {
        const h = req.headers.authorization || '';
        const token = h.startsWith('Bearer ') ? h.slice(7) : null;
        if (!token) return res.status(401).json({ success: false, error: 'Token ausente.' });
        try {
            const d = jwt.verify(token, SECRET());
            const [rows] = await db.execute(
                'SELECT id, username, is_admin, is_banned FROM accounts WHERE id = ? AND session_token = ? AND session_expires > NOW() LIMIT 1',
                [d.sub, token]);
            if (!rows.length || rows[0].is_banned) return res.status(401).json({ success: false, error: 'Sessao invalida.' });
            req.user = { id: rows[0].id, username: rows[0].username, admin: !!rows[0].is_admin };
            next();
        } catch (e) {
            res.status(401).json({ success: false, error: 'Token invalido.' });
        }
    }

    function admin(req, res, next) {
        if (!req.user.admin) return res.status(403).json({ success: false, error: 'Acesso restrito a administradores.' });
        next();
    }

    const wrap = fn => (req, res) => fn(req, res).catch(e => {
        console.error('[API]', req.method, req.path, e.message);
        res.status(500).json({ success: false, error: 'Erro interno do servidor.' });
    });

    // ---------- Personagens da conta ----------
    app.get('/api/characters', auth, wrap(async (req, res) => {
        const [rows] = await db.execute(
            `SELECT id, slot_index AS slot, name, gender, job, level, map_name AS map, gold
             FROM characters WHERE account_id = ? AND is_deleted = 0 ORDER BY slot_index`, [req.user.id]);
        res.json({ success: true, characters: rows });
    }));

    app.get('/api/characters/:id/inventory', auth, wrap(async (req, res) => {
        const id = parseInt(req.params.id, 10);
        const [own] = await db.execute('SELECT id FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0', [id, req.user.id]);
        if (!own.length) return res.status(404).json({ success: false, error: 'Personagem nao encontrado.' });
        const [rows] = await db.execute(
            `SELECT slot_index AS slot, item_id AS itemId, quantity, durability, refine_level AS refine, is_equipped AS equipped
             FROM inventory WHERE character_id = ? ORDER BY slot_index`, [id]);
        res.json({ success: true, items: rows });
    }));

    // ---------- Administracao (itens sao gerados a partir do iteminfo original, nao criados) ----------
    app.get('/api/admin/me', auth, (req, res) => res.json({ success: true, admin: req.user.admin, username: req.user.username }));

    // Entrega direta no inventario de um personagem (offline ou online sem sessao Mirror).
    app.post('/api/admin/give', auth, admin, wrap(async (req, res) => {
        const characterId = parseInt(req.body.characterId, 10);
        const itemId = parseInt(req.body.itemId, 10);
        const qty = Math.max(1, Math.min(9999, parseInt(req.body.quantity, 10) || 1));
        const refine = Math.max(0, Math.min(12, parseInt(req.body.refine, 10) || 0));
        const sockets = Math.max(0, Math.min(3, parseInt(req.body.sockets, 10) || 0));
        const gems = [0, 1, 2].map(i => i < sockets ? Math.max(0, parseInt((req.body.gems || [])[i], 10) || 0) : null);
        const [c] = await db.execute('SELECT id FROM characters WHERE id = ? AND is_deleted = 0', [characterId]);
        if (!c.length) return res.status(404).json({ success: false, error: 'Personagem nao encontrado.' });
        const [used] = await db.execute('SELECT slot_index FROM inventory WHERE character_id = ?', [characterId]);
        const taken = new Set(used.map(u => u.slot_index));
        let slot = -1;
        for (let i = 0; i < 40; i++) if (!taken.has(i)) { slot = i; break; }
        if (slot < 0) return res.status(409).json({ success: false, error: 'Inventario cheio.' });
        await db.execute(
            `INSERT INTO inventory (unique_item_id, character_id, slot_index, item_id, quantity, durability,             refine_level, is_equipped, gem_slot_1, gem_slot_2, gem_slot_3)
                         VALUES (UUID(), ?, ?, ?, ?, 100, ?, 0, ?, ?, ?)`, [characterId, slot, itemId, qty, refine, gems[0], gems[1], gems[2]]);
        res.json({ success: true, slot });
    }));
};