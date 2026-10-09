// API de jogo (/api/game): o cliente Unity nunca acessa o banco, so esta API (autenticada por JWT).
const jwt = require('jsonwebtoken');
const rateLimit = require('express-rate-limit');

const BASE_STATS = {
    0: [15, 10, 12, 8, 10, 150, 50, 100],
    1: [10, 15, 10, 10, 12, 120, 60, 100],
    2: [8, 10, 10, 15, 10, 100, 80, 100],
    3: [10, 12, 10, 12, 11, 110, 70, 100],
};
BASE_STATS[4] = BASE_STATS[0]; // NewCharacterTest inherits Lance's starting attributes.
const DEFAULT_STATS = [10, 10, 10, 10, 10, 100, 50, 100];
const MAX_INV = 300, MAX_SKILLS = 300;

const int = (v, d = 0) => { const n = parseInt(v, 10); return Number.isFinite(n) ? n : d; };
const num = (v, d = 0) => { const n = Number(v); return Number.isFinite(n) ? n : d; };
const opt = v => (v === undefined || v === null || v < 0 ? null : v);

module.exports = function register(app, db) {
    const limiter = rateLimit({ windowMs: 60 * 1000, max: 600, standardHeaders: true, legacyHeaders: false });

    async function auth(req, res, next) {
        const h = req.headers.authorization || '';
        const token = h.startsWith('Bearer ') ? h.slice(7) : null;
        if (!token) return res.status(401).json({ success: false, error: 'UNAUTHORIZED' });
        try {
            const d = jwt.verify(token, process.env.JWT_SECRET);
            const [rows] = await db.execute(
                'SELECT id, username, is_admin, is_banned FROM accounts WHERE id = ? AND session_token = ? AND session_expires > NOW() LIMIT 1',
                [d.sub, token]);
            if (!rows.length || rows[0].is_banned) return res.status(401).json({ success: false, error: 'UNAUTHORIZED' });
            req.user = { id: rows[0].id, username: rows[0].username, admin: !!rows[0].is_admin };
            next();
        } catch (e) {
            res.status(401).json({ success: false, error: 'UNAUTHORIZED' });
        }
    }
    const wrap = fn => (req, res) => fn(req, res).catch(e => {
        console.error('[Game]', req.method, req.path, e.message);
        res.status(500).json({ success: false, error: 'DB_ERROR' });
    });
    const owned = async (charId, accountId) => {
        const [r] = await db.execute('SELECT id FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0', [charId, accountId]);
        return r.length > 0;
    };

    const g = '/api/game';
    app.use(g, limiter, auth);

    app.get(`${g}/me`, (req, res) => res.json({ success: true, admin: req.user.admin, username: req.user.username, accountId: req.user.id }));

    app.get(`${g}/characters`, wrap(async (req, res) => {
        const [rows] = await db.execute(
            `SELECT id, slot_index, name, gender, job, level, map_name, pos_x, pos_y, pos_z, rotation_y,
                    hair_style, hair_color, face_style, UNIX_TIMESTAMP(last_online) AS last_online,
                    (SELECT GROUP_CONCAT(i.item_id) FROM inventory i WHERE i.character_id = characters.id AND i.is_equipped = 1) AS equipped
             FROM characters WHERE account_id = ? AND is_deleted = 0 ORDER BY slot_index`, [req.user.id]);
        res.json({
            success: true,
            Characters: rows.map(r => ({
                Id: Number(r.id), SlotIndex: r.slot_index, Name: r.name, Gender: r.gender, Job: r.job, Level: r.level,
                MapName: r.map_name || 'garner', PosX: r.pos_x ?? 0, PosY: r.pos_y ?? 1.5, PosZ: r.pos_z ?? 0, RotationY: r.rotation_y ?? 0,
                HairStyle: r.hair_style || 0, HairColor: r.hair_color || 0, FaceStyle: r.face_style || 0,
                Equipped: r.equipped || '', LastOnline: Number(r.last_online || 0),
            })),
        });
    }));

    app.post(`${g}/characters`, wrap(async (req, res) => {
        const b = req.body || {};
        const name = typeof b.name === 'string' ? b.name.trim() : '';
        if (name.length < 3 || name.length > 16 || /[\u0000-\u001f<>]/.test(name)) return res.json({ success: false, error: 'NAME_INVALID' });
        const slot = int(b.slot), job = int(b.job), gender = int(b.gender);
        if (slot < 0 || slot > 5) return res.json({ success: false, error: 'SLOT_INVALID' });
        const [s] = await db.execute('SELECT COUNT(*) AS c FROM characters WHERE account_id = ? AND slot_index = ? AND is_deleted = 0', [req.user.id, slot]);
        if (s[0].c > 0) return res.json({ success: false, error: 'SLOT_OCCUPIED' });
        const [n] = await db.execute('SELECT COUNT(*) AS c FROM characters WHERE name = ? AND is_deleted = 0', [name]);
        if (n[0].c > 0) return res.json({ success: false, error: 'NAME_TAKEN' });
        const [str, agi, con, spr, sta, hp, mp, sp] = BASE_STATS[job] || DEFAULT_STATS;
        const c = b.city || {};
        const [r] = await db.execute(
            `INSERT INTO characters (account_id, slot_index, name, gender, job, level, base_str, base_agi, base_con, base_spr, base_sta,
                max_hp, max_mp, max_sp, current_hp, current_mp, current_sp, map_name, pos_x, pos_y, pos_z, rotation_y,
                hair_style, hair_color, face_style, created_at)
             VALUES (?, ?, ?, ?, ?, 1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW())`,
            [req.user.id, slot, name, gender, job, str, agi, con, spr, sta, hp, mp, sp, hp, mp, sp,
                String(c.map || 'garner').slice(0, 64), num(c.x), num(c.y, 1.5), num(c.z), num(c.rotY),
                int(b.hairStyle), int(b.hairColor), int(b.faceStyle)]);
        res.json({ success: true, charId: Number(r.insertId) });
    }));

    app.get(`${g}/characters/:id`, wrap(async (req, res) => {
        const id = int(req.params.id);
        const [rows] = await db.execute('SELECT * FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0', [id, req.user.id]);
        if (!rows.length) return res.json({ success: false, error: 'NOT_FOUND' });
        const r = rows[0];
        const [inv] = await db.execute(
            `SELECT id, unique_item_id, slot_index, item_id, quantity, durability, is_equipped, is_locked, owner_character_id,
                    refine_level, gem_slot_1, gem_slot_2, gem_slot_3 FROM inventory WHERE character_id = ?`, [id]);
        const [sk] = await db.execute('SELECT id, skill_id, level, exp FROM skills WHERE character_id = ?', [id]);
        res.json({
            success: true,
            Character: {
                Id: Number(r.id), AccountId: Number(r.account_id), Name: r.name, Job: r.job, Gender: r.gender, Level: r.level, Exp: Number(r.exp || 0),
                CurrentHp: r.current_hp, CurrentMp: r.current_mp, CurrentSp: r.current_sp, MaxHp: r.max_hp, MaxMp: r.max_mp, MaxSp: r.max_sp,
                BaseStr: r.base_str, BaseAgi: r.base_agi, BaseCon: r.base_con, BaseSpr: r.base_spr, BaseSta: r.base_sta,
                Gold: Number(r.gold || 0), StatPoints: r.stat_points || 0, SkillPoints: r.skill_points || 0, PkPoints: r.pk_points || 0, Reputation: r.reputation || 0,
                HairStyle: r.hair_style || 0, HairColor: r.hair_color || 0, FaceStyle: r.face_style || 0,
                MapName: r.map_name || 'garner', PosX: r.pos_x, PosY: r.pos_y, PosZ: r.pos_z, RotationY: r.rotation_y,
            },
            Inventory: inv.map(i => ({
                Id: Number(i.id), UniqueItemId: i.unique_item_id || '', SlotIndex: i.slot_index, ItemId: i.item_id, Quantity: i.quantity,
                Durability: i.durability ?? 100, IsEquipped: !!i.is_equipped, IsLocked: !!i.is_locked,
                OwnerCharacterId: i.owner_character_id == null ? -1 : Number(i.owner_character_id),
                RefineLevel: i.refine_level || 0,
                Gem1: i.gem_slot_1 == null ? -1 : i.gem_slot_1, Gem2: i.gem_slot_2 == null ? -1 : i.gem_slot_2, Gem3: i.gem_slot_3 == null ? -1 : i.gem_slot_3,
            })),
            Skills: sk.map(s => ({ Id: Number(s.id), SkillId: s.skill_id, Level: s.level, Exp: Number(s.exp || 0) })),
        });
    }));

    app.put(`${g}/characters/:id`, wrap(async (req, res) => {
        const id = int(req.params.id);
        if (!(await owned(id, req.user.id))) return res.json({ success: false, error: 'NOT_FOUND' });
        const c = (req.body || {}).Character || {};
        const inv = (req.body || {}).Inventory;
        const skills = (req.body || {}).Skills;
        if ((inv && (!Array.isArray(inv) || inv.length > MAX_INV)) || (skills && (!Array.isArray(skills) || skills.length > MAX_SKILLS)))
            return res.json({ success: false, error: 'INVALID' });
        const conn = await db.getConnection();
        try {
            await conn.beginTransaction();
            await conn.execute(
                `UPDATE characters SET level=?, exp=?, current_hp=?, current_mp=?, current_sp=?, pos_x=?, pos_y=?, pos_z=?, rotation_y=?, map_name=?,
                    base_str=?, base_agi=?, base_con=?, base_spr=?, base_sta=?, max_hp=?, max_mp=?, max_sp=?,
                    gold=?, stat_points=?, skill_points=?, pk_points=?, reputation=?, last_online=NOW() WHERE id = ? AND account_id = ?`,
                [Math.max(1, int(c.Level, 1)), Math.max(0, num(c.Exp)), int(c.CurrentHp), int(c.CurrentMp), int(c.CurrentSp),
                    num(c.PosX), num(c.PosY), num(c.PosZ), num(c.RotationY), String(c.MapName || 'garner').slice(0, 64),
                    int(c.BaseStr), int(c.BaseAgi), int(c.BaseCon), int(c.BaseSpr), int(c.BaseSta), int(c.MaxHp), int(c.MaxMp), int(c.MaxSp),
                    Math.max(0, num(c.Gold)), int(c.StatPoints), int(c.SkillPoints), int(c.PkPoints), int(c.Reputation), id, req.user.id]);
            if (inv) {
                await conn.execute('DELETE FROM inventory WHERE character_id = ?', [id]);
                for (const i of inv) {
                    await conn.execute(
                        `INSERT INTO inventory (unique_item_id, character_id, slot_index, item_id, quantity, durability, is_equipped, is_locked,
                            owner_character_id, refine_level, gem_slot_1, gem_slot_2, gem_slot_3)
                         VALUES (COALESCE(NULLIF(?, ''), UUID()), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                        [String(i.UniqueItemId || '').slice(0, 64), id, int(i.SlotIndex), int(i.ItemId), Math.max(1, int(i.Quantity, 1)), int(i.Durability, 100),
                            i.IsEquipped ? 1 : 0, i.IsLocked ? 1 : 0, opt(i.OwnerCharacterId), int(i.RefineLevel),
                            opt(i.Gem1), opt(i.Gem2), opt(i.Gem3)]);
                }
            }
            if (skills) {
                await conn.execute('DELETE FROM skills WHERE character_id = ?', [id]);
                for (const s of skills)
                    await conn.execute('INSERT INTO skills (character_id, skill_id, level, exp) VALUES (?, ?, ?, ?)', [id, int(s.SkillId), int(s.Level), Math.max(0, num(s.Exp))]);
            }
            await conn.commit();
            res.json({ success: true });
        } catch (e) {
            await conn.rollback();
            throw e;
        } finally { conn.release(); }
    }));

    app.post(`${g}/characters/:id/delete`, wrap(async (req, res) => {
        const id = int(req.params.id);
        const password = (req.body || {}).password;
        if (typeof password !== 'string' || !password) return res.json({ success: false, error: 'INVALID_PASSWORD' });
        const [rows] = await db.execute('SELECT password_hash FROM accounts WHERE id = ?', [req.user.id]);
        if (!(await owned(id, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        if (!(await require('bcryptjs').compare(password, rows[0].password_hash))) return res.json({ success: false, error: 'INVALID_PASSWORD' });
        const [r] = await db.execute(
            "UPDATE characters SET is_deleted = 1, deleted_at = NOW(), name = CONCAT(name, '_deleted_', UNIX_TIMESTAMP()) WHERE id = ? AND account_id = ?", [id, req.user.id]);
        res.json(r.affectedRows > 0 ? { success: true } : { success: false, error: 'DELETE_FAILED' });
    }));

    // =================================================================
    // AMIGOS
    // =================================================================
    app.get(`${g}/friends/:charId`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [rows] = await db.execute(
            `SELECT c.id, c.name, c.level, c.job, UNIX_TIMESTAMP(c.last_online) AS last_online
             FROM friendships f JOIN characters c ON c.id = f.friend_character_id
             WHERE f.character_id = ? AND c.is_deleted = 0`, [charId]);
        res.json({ success: true, Friends: rows.map(r => ({ Id: Number(r.id), Name: r.name, Level: r.level, Job: r.job, LastOnline: Number(r.last_online || 0) })) });
    }));

    app.post(`${g}/friends/:charId/add`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        const targetName = String((req.body || {}).name || '').trim();
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [[target]] = [await db.execute('SELECT id, name FROM characters WHERE name = ? AND is_deleted = 0', [targetName])];
        if (!target || !target.length) return res.json({ success: false, error: 'PLAYER_NOT_FOUND' });
        const targetId = target[0].id;
        if (targetId === charId) return res.json({ success: false, error: 'CANNOT_ADD_SELF' });
        try {
            await db.execute('INSERT INTO friendships (character_id, friend_character_id) VALUES (?, ?)', [charId, targetId]);
            await db.execute('INSERT IGNORE INTO friendships (character_id, friend_character_id) VALUES (?, ?)', [targetId, charId]);
            res.json({ success: true });
        } catch (e) {
            if (e.code === 'ER_DUP_ENTRY') return res.json({ success: false, error: 'ALREADY_FRIENDS' });
            throw e;
        }
    }));

    app.post(`${g}/friends/:charId/remove`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        const targetId = int((req.body || {}).friendId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        await db.execute('DELETE FROM friendships WHERE character_id = ? AND friend_character_id = ?', [charId, targetId]);
        await db.execute('DELETE FROM friendships WHERE character_id = ? AND friend_character_id = ?', [targetId, charId]);
        res.json({ success: true });
    }));

    // =================================================================
    // CORREIO (MAIL)
    // =================================================================
    app.get(`${g}/mail/:charId`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [rows] = await db.execute(
            `SELECT id, sender_name, subject, body, gold, item_id, item_quantity, item_refine, is_read, is_claimed,
                    UNIX_TIMESTAMP(created_at) AS created_at
             FROM mails WHERE recipient_id = ? ORDER BY created_at DESC LIMIT 100`, [charId]);
        res.json({
            success: true,
            Mails: rows.map(r => ({
                Id: Number(r.id), SenderName: r.sender_name, Subject: r.subject, Body: r.body, Gold: Number(r.gold),
                ItemId: r.item_id, ItemQuantity: r.item_quantity, ItemRefine: r.item_refine,
                IsRead: !!r.is_read, IsClaimed: !!r.is_claimed, CreatedAt: Number(r.created_at || 0),
            })),
        });
    }));

    app.post(`${g}/mail/:charId/send`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const b = req.body || {};
        const targetName = String(b.targetName || '').trim();
        const [senderRows] = await db.execute('SELECT name, gold FROM characters WHERE id = ?', [charId]);
        if (!senderRows.length) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const gold = Math.max(0, num(b.gold, 0));
        if (gold > senderRows[0].gold) return res.json({ success: false, error: 'INSUFFICIENT_GOLD' });
        const [targetRows] = await db.execute('SELECT id FROM characters WHERE name = ? AND is_deleted = 0', [targetName]);
        if (!targetRows.length) return res.json({ success: false, error: 'PLAYER_NOT_FOUND' });
        if (gold > 0) await db.execute('UPDATE characters SET gold = gold - ? WHERE id = ?', [gold, charId]);
        await db.execute(
            `INSERT INTO mails (sender_id, sender_name, recipient_id, subject, body, gold, item_id, item_quantity, item_refine)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [charId, senderRows[0].name, targetRows[0].id, String(b.subject || '').slice(0, 80), String(b.body || '').slice(0, 1000),
                gold, opt(b.itemId) ?? -1, int(b.itemQuantity, 0), int(b.itemRefine, 0)]);
        res.json({ success: true });
    }));

    app.post(`${g}/mail/:charId/:mailId/claim`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        const mailId = int(req.params.mailId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [rows] = await db.execute('SELECT * FROM mails WHERE id = ? AND recipient_id = ? AND is_claimed = 0', [mailId, charId]);
        if (!rows.length) return res.json({ success: false, error: 'MAIL_NOT_FOUND' });
        const mail = rows[0];
        if (mail.gold > 0) await db.execute('UPDATE characters SET gold = gold + ? WHERE id = ?', [mail.gold, charId]);
        await db.execute('UPDATE mails SET is_claimed = 1, is_read = 1 WHERE id = ?', [mailId]);
        res.json({
            success: true, Gold: Number(mail.gold), ItemId: mail.item_id,
            ItemQuantity: mail.item_quantity, ItemRefine: mail.item_refine,
        });
    }));

    app.post(`${g}/mail/:charId/:mailId/read`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        const mailId = int(req.params.mailId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        await db.execute('UPDATE mails SET is_read = 1 WHERE id = ? AND recipient_id = ?', [mailId, charId]);
        res.json({ success: true });
    }));

    app.post(`${g}/mail/:charId/:mailId/delete`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        const mailId = int(req.params.mailId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        await db.execute('DELETE FROM mails WHERE id = ? AND recipient_id = ? AND (is_claimed = 1 OR item_id < 0)', [mailId, charId]);
        res.json({ success: true });
    }));

    // =================================================================
    // GUILDA
    // =================================================================
    app.get(`${g}/guild/:charId`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [memberRows] = await db.execute('SELECT guild_id, rank_name FROM guild_members WHERE character_id = ?', [charId]);
        if (!memberRows.length) return res.json({ success: true, InGuild: false });
        const guildId = memberRows[0].guild_id;
        const [[guild]] = [await db.execute('SELECT * FROM guilds WHERE id = ?', [guildId])];
        if (!guild || !guild.length) return res.json({ success: true, InGuild: false });
        const [members] = await db.execute('SELECT character_id, character_name, rank_name FROM guild_members WHERE guild_id = ? ORDER BY rank_name = "Lider" DESC, character_name', [guildId]);
        res.json({
            success: true, InGuild: true, GuildId: guildId, Name: guild[0].name, Notice: guild[0].notice,
            Level: guild[0].level, LeaderCharacterId: Number(guild[0].leader_character_id), MyRank: memberRows[0].rank_name,
            Members: members.map(m => ({ CharacterId: Number(m.character_id), Name: m.character_name, Rank: m.rank_name })),
        });
    }));

    app.post(`${g}/guild/:charId/create`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const name = String((req.body || {}).name || '').trim();
        if (name.length < 3 || name.length > 32) return res.json({ success: false, error: 'INVALID_NAME' });
        const [[existing]] = [await db.execute('SELECT id FROM guild_members WHERE character_id = ?', [charId])];
        if (existing.length) return res.json({ success: false, error: 'ALREADY_IN_GUILD' });
        const [[charRow]] = [await db.execute('SELECT name, gold FROM characters WHERE id = ?', [charId])];
        if (!charRow.length) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const COST = 100000;
        if (charRow[0].gold < COST) return res.json({ success: false, error: 'INSUFFICIENT_GOLD' });
        try {
            const [ins] = await db.execute('INSERT INTO guilds (name, leader_character_id) VALUES (?, ?)', [name, charId]);
            await db.execute('INSERT INTO guild_members (guild_id, character_id, character_name, rank_name) VALUES (?, ?, ?, "Lider")', [ins.insertId, charId, charRow[0].name]);
            await db.execute('UPDATE characters SET gold = gold - ? WHERE id = ?', [COST, charId]);
            res.json({ success: true, GuildId: ins.insertId });
        } catch (e) {
            if (e.code === 'ER_DUP_ENTRY') return res.json({ success: false, error: 'NAME_TAKEN' });
            throw e;
        }
    }));

    app.post(`${g}/guild/:charId/invite`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const targetName = String((req.body || {}).targetName || '').trim();
        const [[membership]] = [await db.execute('SELECT guild_id, rank_name FROM guild_members WHERE character_id = ?', [charId])];
        if (!membership.length || (membership[0].rank_name !== 'Lider' && membership[0].rank_name !== 'Oficial'))
            return res.json({ success: false, error: 'NOT_AUTHORIZED' });
        const [[target]] = [await db.execute('SELECT id, name FROM characters WHERE name = ? AND is_deleted = 0', [targetName])];
        if (!target.length) return res.json({ success: false, error: 'PLAYER_NOT_FOUND' });
        const [[alreadyIn]] = [await db.execute('SELECT id FROM guild_members WHERE character_id = ?', [target[0].id])];
        if (alreadyIn.length) return res.json({ success: false, error: 'TARGET_IN_GUILD' });
        try {
            await db.execute('INSERT INTO guild_members (guild_id, character_id, character_name, rank_name) VALUES (?, ?, ?, "Membro")', [membership[0].guild_id, target[0].id, target[0].name]);
            res.json({ success: true });
        } catch (e) {
            if (e.code === 'ER_DUP_ENTRY') return res.json({ success: false, error: 'TARGET_IN_GUILD' });
            throw e;
        }
    }));

    app.post(`${g}/guild/:charId/leave`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [[membership]] = [await db.execute('SELECT guild_id, rank_name FROM guild_members WHERE character_id = ?', [charId])];
        if (!membership.length) return res.json({ success: false, error: 'NOT_IN_GUILD' });
        const guildId = membership[0].guild_id;
        if (membership[0].rank_name === 'Lider') {
            const [[others]] = [await db.execute('SELECT character_id FROM guild_members WHERE guild_id = ? AND character_id != ? ORDER BY joined_at LIMIT 1', [guildId, charId])];
            await db.execute('DELETE FROM guild_members WHERE character_id = ?', [charId]);
            if (others.length) {
                await db.execute('UPDATE guild_members SET rank_name = "Lider" WHERE character_id = ?', [others[0].character_id]);
                await db.execute('UPDATE guilds SET leader_character_id = ? WHERE id = ?', [others[0].character_id, guildId]);
            } else {
                await db.execute('DELETE FROM guilds WHERE id = ?', [guildId]);
            }
        } else {
            await db.execute('DELETE FROM guild_members WHERE character_id = ?', [charId]);
        }
        res.json({ success: true });
    }));

    app.post(`${g}/guild/:charId/kick`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const targetName = String((req.body || {}).targetName || '').trim();
        const [[membership]] = [await db.execute('SELECT guild_id, rank_name FROM guild_members WHERE character_id = ?', [charId])];
        if (!membership.length || (membership[0].rank_name !== 'Lider' && membership[0].rank_name !== 'Oficial'))
            return res.json({ success: false, error: 'NOT_AUTHORIZED' });
        await db.execute('DELETE FROM guild_members WHERE guild_id = ? AND character_name = ? AND rank_name != "Lider"', [membership[0].guild_id, targetName]);
        res.json({ success: true });
    }));

    app.post(`${g}/guild/:charId/notice`, wrap(async (req, res) => {
        const charId = int(req.params.charId);
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [[membership]] = [await db.execute('SELECT guild_id, rank_name FROM guild_members WHERE character_id = ?', [charId])];
        if (!membership.length || membership[0].rank_name !== 'Lider') return res.json({ success: false, error: 'NOT_AUTHORIZED' });
        await db.execute('UPDATE guilds SET notice = ? WHERE id = ?', [String((req.body || {}).notice || '').slice(0, 400), membership[0].guild_id]);
        res.json({ success: true });
    }));

    // ---- Quests ----
    const questNumber = (value, minimum = 1) =>
        Number.isInteger(value) && value >= minimum && value <= 2147483647;

    app.get(`${g}/quests/:charId`, wrap(async (req, res) => {
        const charId = Number(req.params.charId);
        if (!Number.isSafeInteger(charId) || charId <= 0) return res.json({ success: false, error: 'INVALID_CHARACTER' });
        if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
        const [rows] = await db.execute('SELECT quest_id, progress, completed_at FROM quest_progress WHERE character_id = ?', [charId]);
        res.json({
            success: true,
            active: rows.filter(r => !r.completed_at).map(r => ({ questId: r.quest_id, progress: r.progress })),
            completed: rows.filter(r => r.completed_at).map(r => r.quest_id),
        });
    }));

    for (const action of ['accept', 'abandon', 'progress', 'complete']) {
        app.post(`${g}/quests/:charId/${action}`, wrap(async (req, res) => {
            const charId = Number(req.params.charId), b = req.body || {};
            if (!Number.isSafeInteger(charId) || charId <= 0) return res.json({ success: false, error: 'INVALID_CHARACTER' });
            if (!(await owned(charId, req.user.id))) return res.json({ success: false, error: 'CHARACTER_NOT_FOUND' });
            if (!questNumber(b.questId) || (action === 'progress' && !questNumber(b.progress, 0)))
                return res.json({ success: false, error: 'INVALID_QUEST' });
            let result;
            if (action === 'accept') {
                [result] = await db.execute('INSERT IGNORE INTO quest_progress (character_id, quest_id, progress) VALUES (?, ?, 0)', [charId, b.questId]);
            } else if (action === 'abandon') {
                [result] = await db.execute('DELETE FROM quest_progress WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL', [charId, b.questId]);
            } else if (action === 'progress') {
                [result] = await db.execute('UPDATE quest_progress SET progress = GREATEST(progress, ?) WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL',
                    [b.progress, charId, b.questId]);
            } else {
                [result] = await db.execute('UPDATE quest_progress SET completed_at = NOW() WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL', [charId, b.questId]);
            }
            return result.affectedRows > 0 ? res.json({ success: true })
                : res.json({ success: false, error: action === 'accept' ? 'ALREADY_HAVE_QUEST' : 'QUEST_NOT_ACTIVE' });
        }));
    }

    app.post(`${g}/audit`, wrap(async (req, res) => {
        const b = req.body || {};
        let data = typeof b.detail === 'string' ? b.detail.slice(0, 4000) : '{}';
        try { JSON.parse(data); } catch { data = JSON.stringify({ text: data }); }
        await db.execute(
            'INSERT INTO audit_log (account_id, character_id, action_type, action_data, ip_address, created_at) VALUES (?, ?, ?, ?, ?, NOW())',
            [req.user.id, b.charId > 0 ? int(b.charId) : null, String(b.action || '').slice(0, 64), data, req.ip]);
        res.json({ success: true });
    }));
};
