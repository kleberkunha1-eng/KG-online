// API de jogo (/api/game): o cliente Unity nunca acessa o banco, so esta API (autenticada por JWT).
const jwt = require('jsonwebtoken');
const rateLimit = require('express-rate-limit');
const { createHash } = require('node:crypto');

const BASE_STATS = {
    0: [15, 10, 12, 8, 10, 150, 50, 100],
    1: [10, 15, 10, 10, 12, 120, 60, 100],
    2: [8, 10, 10, 15, 10, 100, 80, 100],
    3: [10, 12, 10, 12, 11, 110, 70, 100],
};
BASE_STATS[4] = BASE_STATS[0]; // NewCharacterTest inherits Lance's starting attributes.
const DEFAULT_STATS = [10, 10, 10, 10, 10, 100, 50, 100];
const MAX_INV = 300, MAX_SKILLS = 300;


const gameplayStateValid = state => state && state.Version === 1 && typeof state.StallName === 'string' && state.StallName.length <= 32
    && Array.isArray(state.Fairies) && state.Fairies.length <= 300 && new Set(state.Fairies.map(f=>f && f.ItemKey)).size===state.Fairies.length
    && state.Fairies.every(f=>f && typeof f.ItemKey==='string' && f.ItemKey.length<=64 && ['Growth','Stamina','Strength','Agility','Accuracy','Constitution','Spirit'].every(k=>Number.isInteger(f[k]) && f[k]>=0) && f.Growth<=6480 && f.Stamina<=32000 && f.Strength+f.Agility+f.Accuracy+f.Constitution+f.Spirit<=42)
    && Array.isArray(state.Offers) && state.Offers.length <= 24 && new Set(state.Offers.map(o=>o && o.ItemKey)).size===state.Offers.length
    && state.Offers.every(o=>o && typeof o.ItemKey==='string' && o.ItemKey.length<=64 && Number.isInteger(o.ItemId) && o.ItemId>0 && Number.isInteger(o.Quantity) && o.Quantity>0 && o.Quantity<=100000 && Number.isSafeInteger(o.Price) && o.Price>0 && o.Price*o.Quantity<=1000000000);

const STALL_COLS = ['unique_item_id','character_id','slot_index','item_id','quantity','durability','fusion_item_id','medal_honor','medal_wins','medal_entries','medal_kills','medal_deaths','is_equipped','is_locked','owner_character_id','refine_level','gem_slot_1','gem_slot_2','gem_slot_3'];
function stallSale(b,buyer,seller,item,state,occupied) {
    if (!buyer || !seller || buyer.id === seller.id || buyer.is_deleted || seller.is_deleted || buyer.save_revision !== b.buyerRevision || seller.save_revision !== b.sellerRevision) throw Error('SAVE_CONFLICT');
    const offer = state && state.Offers && state.Offers.find(o=>o.ItemKey===b.itemKey);
    if (!state || state.Version!==1 || !state.StallName || !offer || !item || item.is_equipped || item.is_locked || (item.owner_character_id != null && item.owner_character_id >= 0) || occupied || offer.Quantity < b.quantity || item.quantity < b.quantity || !Number.isSafeInteger(offer.Price) || offer.Price<=0) throw Error('INVALID_OFFER');
    const total=offer.Price*b.quantity;
    if (!Number.isSafeInteger(total) || total<=0 || Number(buyer.gold)<total || !Number.isSafeInteger(Number(seller.gold)+total)) throw Error('INSUFFICIENT_GOLD');
    offer.Quantity-=b.quantity;
    state.Offers=state.Offers.filter(o=>o.Quantity>0);
    if (!state.Offers.length) state.StallName='';
    const moved={...item,unique_item_id:b.operationId,character_id:buyer.id,slot_index:b.buyerSlot,quantity:b.quantity};
    return {state,moved,total,response:{success:true,gameplayStateVersion:1,buyerRevision:b.buyerRevision+1,sellerRevision:b.sellerRevision+1,buyerGold:Number(buyer.gold)-total,sellerGold:Number(seller.gold)+total,itemUniqueId:b.operationId}};
}
const validStallRequest = b => b && typeof b.operationId === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(b.operationId) && ['sellerId','buyerRevision','sellerRevision','quantity','buyerSlot'].every(k=>Number.isSafeInteger(b[k])) && b.sellerId>0 && b.buyerRevision>=0 && b.sellerRevision>=0 && b.quantity>0 && b.quantity<=100000 && b.buyerSlot>=0 && b.buyerSlot<300 && typeof b.itemKey === 'string' && b.itemKey.length<=64;


const int = (v, d = 0) => { const n = parseInt(v, 10); return Number.isFinite(n) ? n : d; };
const num = (v, d = 0) => { const n = Number(v); return Number.isFinite(n) ? n : d; };
const opt = v => (v === undefined || v === null || v < 0 ? null : v);

const questStateValid = state => state && state.Version === 1
    && Array.isArray(state.Records) && state.Records.length <= 4096
    && new Set(state.Records).size === state.Records.length
    && state.Records.every(n => Number.isInteger(n) && n > 0 && n <= 65535)
    && Array.isArray(state.Missions) && state.Missions.length <= 64
    && new Set(state.Missions.map(m => m && m.Id)).size === state.Missions.length
    && state.Missions.every(m => m && Number.isInteger(m.Id) && m.Id > 0 && m.Id <= 65535
        && Number.isInteger(m.DefinitionId) && m.DefinitionId > 0 && m.DefinitionId <= 65535
        && Array.isArray(m.Flags) && m.Flags.length <= 1024 && new Set(m.Flags).size === m.Flags.length
        && m.Flags.every(n => Number.isInteger(n) && n >= 0 && n < 1024)
        && Array.isArray(m.Triggers) && m.Triggers.length <= 64 && new Set(m.Triggers).size === m.Triggers.length
        && m.Triggers.every(n => Number.isInteger(n) && n > 0 && n <= 2147483647));

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

    app.get(`${g}/capabilities`, wrap(async (req,res) => {
        await db.execute('SELECT gameplay_state_json FROM characters LIMIT 0');
        await db.execute('SELECT operation_id FROM stall_purchase_receipts LIMIT 0');
        return res.json({success:true,gameplayStateVersion:1,guildVersion:1,forgeVersion:1,fairyVersion:1,stallsVersion:1,genericSkillsVersion:1});
    }));

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
                    refine_level, gem_slot_1, gem_slot_2, gem_slot_3, fusion_item_id, medal_honor, medal_wins, medal_entries, medal_kills, medal_deaths FROM inventory WHERE character_id = ?`, [id]);
        const [sk] = await db.execute('SELECT id, skill_id, level, exp FROM skills WHERE character_id = ?', [id]);
        res.json({
            success: true,
            Character: {
                Id: Number(r.id), AccountId: Number(r.account_id), Name: r.name, Job: r.job, Gender: r.gender, Level: r.level, Exp: Number(r.exp || 0),
                SaveRevision: Number(r.save_revision || 0),
                Boats: JSON.parse(r.boats_json || '[]'),
                BankStorageVersion: r.bank_json !== undefined ? 1 : 0,
            GameplayStateVersion: r.gameplay_state_json !== undefined ? 1 : 0,
            Gameplay: JSON.parse(r.gameplay_state_json || '{"Version":1,"Fairies":[],"Offers":[],"StallName":""}'),
                QuestStateVersion: r.quest_state_json !== undefined ? 1 : 0,
                OriginalQuests: JSON.parse(r.quest_state_json || '{"Version":1,"Records":[],"Missions":[]}'),
                BoatOwnershipVersion: r.boats_json !== undefined ? 1 : 0,
                BoatServicesVersion: r.boats_json !== undefined ? 1 : 0,
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
                FusionItemId: i.fusion_item_id || 0,
                MedalHonor: i.medal_honor || 0, MedalWins: i.medal_wins || 0, MedalEntries: i.medal_entries || 0, MedalKills: i.medal_kills || 0, MedalDeaths: i.medal_deaths || 0,
            })),
            BankItems: JSON.parse(r.bank_json || '[]'),
            Skills: sk.map(s => ({ Id: Number(s.id), SkillId: s.skill_id, Level: s.level, Exp: Number(s.exp || 0) })),
        });
    }));

    app.put(`${g}/characters/:id`, wrap(async (req, res) => {
        const id = Number(req.params.id);
        if (!Number.isSafeInteger(id) || id <= 0) return res.json({ success: false, error: 'INVALID_CHARACTER' });
        if (!(await owned(id, req.user.id))) return res.json({ success: false, error: 'NOT_FOUND' });
        const c = (req.body || {}).Character || {};
        const inv = (req.body || {}).Inventory;
        const skills = (req.body || {}).Skills;
        const operationId = (req.body || {}).OperationId;
        const questId = (req.body || {}).QuestId === undefined ? 0 : req.body.QuestId;
        if (typeof operationId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(operationId)
            || !Number.isSafeInteger(c.SaveRevision) || c.SaveRevision < 0 || c.SaveRevision >= Number.MAX_SAFE_INTEGER
            || !Number.isInteger(questId) || questId < 0 || questId > 2147483647)
            return res.json({ success: false, error: 'INVALID_TRANSACTION' });
        const hash = createHash('sha256').update(JSON.stringify(req.body)).digest('hex');
        if ((inv && (!Array.isArray(inv) || inv.length > MAX_INV || inv.some(i => !i || ["MedalHonor","MedalWins","MedalEntries","MedalKills","MedalDeaths"].some(f => i[f] !== undefined && (!Number.isInteger(i[f]) || i[f] < -2147483648 || i[f] > 2147483647)) || (i.FusionItemId !== undefined && (!Number.isInteger(i.FusionItemId) || i.FusionItemId < 0))))) || (skills && (!Array.isArray(skills) || skills.length > MAX_SKILLS)))
            return res.json({ success: false, error: 'INVALID' });
        if (questId > 0 && (!Array.isArray(inv) || !Array.isArray(skills)))
            return res.json({ success: false, error: 'INVALID_TRANSACTION' });
        if (!Number.isSafeInteger(c.Gold) || c.Gold < 0 || !Number.isSafeInteger(c.Exp) || c.Exp < 0)
            return res.json({ success: false, error: 'INVALID_CHARACTER_VALUES' });
        const boats = c.Boats;
        const bank = (req.body || {}).BankItems;
        const bankVersion = c.BankStorageVersion;
        const questVersion = c.QuestStateVersion;
        const gameplayVersion = c.GameplayStateVersion;
        if (gameplayVersion !== undefined && gameplayVersion !== 0 && gameplayVersion !== 1) return res.json({ success: false, error: 'INVALID_GAMEPLAY_STATE' });
        if (gameplayVersion === 1 && !gameplayStateValid(c.Gameplay)) return res.json({ success: false, error: 'INVALID_GAMEPLAY_STATE' });
        if (questVersion !== undefined && questVersion !== 0 && questVersion !== 1)
            return res.json({ success: false, error: 'INVALID_QUEST_STATE' });
        if (questVersion === 1 && (!questStateValid(c.OriginalQuests) || !Number.isInteger(c.Job) || c.Job < 0 || c.Job > 18))
            return res.json({ success: false, error: 'INVALID_QUEST_STATE' });
        if (bankVersion !== undefined && bankVersion !== 0 && bankVersion !== 1)
            return res.json({ success: false, error: 'INVALID_BANK' });
        if (bankVersion === 1 && (!Array.isArray(bank) || bank.length > 32
            || new Set(bank.map(item => item && item.SlotIndex)).size !== bank.length
            || new Set(bank.map(item => item && item.UniqueItemId)).size !== bank.length
            || bank.some(item => !item || !Number.isInteger(item.SlotIndex) || item.SlotIndex < 0 || item.SlotIndex >= 32
                || typeof item.UniqueItemId !== 'string' || item.UniqueItemId.length > 64
                || !Number.isInteger(item.ItemId) || item.ItemId <= 0 || !Number.isInteger(item.Quantity) || item.Quantity <= 0
                || item.Quantity > 100000 || item.IsEquipped || item.IsLocked
                || (Number.isInteger(item.OwnerCharacterId) && item.OwnerCharacterId >= 0 && item.OwnerCharacterId !== id))))
            return res.json({ success: false, error: 'INVALID_BANK' });
        if (bankVersion === 1 && Array.isArray(inv)
            && new Set([...bank.map(item => item.UniqueItemId), ...inv.map(item => item && item.UniqueItemId).filter(Boolean)]).size
                !== bank.length + inv.filter(item => item && item.UniqueItemId).length)
            return res.json({ success: false, error: 'INVALID_BANK' });
        if (boats !== undefined && (!Array.isArray(boats) || boats.length > 3
            || new Set(boats.map(boat => boat && boat.Id)).size !== boats.length
            || boats.some(boat => !boat || typeof boat.Id !== 'string'
                || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(boat.Id)
                || typeof boat.Name !== 'string' || !/^[\x20-\x7e]{2,16}$/.test(boat.Name) || /[<>]/.test(boat.Name)
                || ['TypeId','BerthId','Level','HullId','EngineId','BowId','CannonId','ComponentId','Health','Fuel']
                    .some(key => !Number.isInteger(boat[key]) || boat[key] < 0 || boat[key] > 2147483647)
                || boat.TypeId < 1 || boat.BerthId < 0 || boat.Level < 1 || boat.Level > 100
                || (boat.IsSunk !== undefined && typeof boat.IsSunk !== 'boolean')
                || (boat.Cargo !== undefined && (!Array.isArray(boat.Cargo) || boat.Cargo.length > 4
                    || new Set(boat.Cargo.map(item => item && item.ItemId)).size !== boat.Cargo.length
                    || boat.Cargo.some(item => !item || ![4547,4548,4549,4550].includes(item.ItemId)
                        || !Number.isInteger(item.Quantity) || item.Quantity <= 0 || item.Quantity > 10000))))))
            return res.json({ success: false, error: 'INVALID_BOATS' });
        const conn = await db.getConnection();
        try {
            await conn.beginTransaction();
            const [characters] = await conn.execute('SELECT save_revision, gold, name FROM characters WHERE id = ? AND account_id = ? AND is_deleted = 0 FOR UPDATE', [id, req.user.id]);
            if (!characters.length) { await conn.rollback(); return res.json({ success: false, error: 'NOT_FOUND' }); }
            const [receipts] = await conn.execute('SELECT payload_hash, expected_revision FROM character_save_receipts WHERE character_id = ? AND operation_id = ?', [id, operationId]);
            if (receipts.length) {
                await conn.rollback();
                return res.json(receipts[0].payload_hash === hash
                    ? { success: true, revision: Number(receipts[0].expected_revision) + 1, boatOwnershipVersion: 1, boatServicesVersion: 1, bankStorageVersion: bankVersion === 1 ? 1 : 0, questStateVersion: questVersion === 1 ? 1 : 0, gameplayStateVersion: gameplayVersion === 1 ? 1 : 0 }
                    : { success: false, error: 'OPERATION_MISMATCH' });
            }
            if (Number(characters[0].save_revision) !== c.SaveRevision) {
                await conn.rollback(); return res.json({ success: false, error: 'SAVE_CONFLICT' });
            }
            if (c.GuildCreateName) {
                const name = String(c.GuildCreateName).trim();
                const [stones] = await conn.execute('SELECT COALESCE(SUM(quantity),0) AS n FROM inventory WHERE character_id = ? AND item_id = 1780 AND is_equipped = 0 AND is_locked = 0', [id]);
                const [membership] = await conn.execute('SELECT id FROM guild_members WHERE character_id = ?', [id]);
                const remaining = (inv || []).filter(i => i.ItemId === 1780).reduce((n,i) => n+i.Quantity, 0);
                if (name.length < 3 || name.length > 32 || membership.length || Number(characters[0].gold) < 100000 || c.Gold > Number(characters[0].gold)-100000 || remaining !== Number(stones[0].n)-1) {
                    await conn.rollback(); return res.json({success:false,error:'INVALID_GUILD_CREATION'});
                }
                const [guild] = await conn.execute('INSERT INTO guilds (name, leader_character_id) VALUES (?,?)',[name,id]);
                await conn.execute('INSERT INTO guild_members (guild_id,character_id,character_name,rank_name) VALUES (?,?,?,"Lider")',[guild.insertId,id,characters[0].name]);
            }
            if (questId > 0) {
                const [result] = await conn.execute('UPDATE quest_progress SET completed_at = NOW() WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL', [id, questId]);
                if (!result.affectedRows) { await conn.rollback(); return res.json({ success: false, error: 'QUEST_NOT_ACTIVE' }); }
            }
            await conn.execute('INSERT INTO character_save_receipts (character_id, operation_id, payload_hash, expected_revision, quest_id) VALUES (?, ?, ?, ?, ?)',
                [id, operationId, hash, c.SaveRevision, questId]);
            await conn.execute(
                `UPDATE characters SET save_revision=save_revision+1, level=?, exp=?, current_hp=?, current_mp=?, current_sp=?, pos_x=?, pos_y=?, pos_z=?, rotation_y=?, map_name=?,
                    base_str=?, base_agi=?, base_con=?, base_spr=?, base_sta=?, max_hp=?, max_mp=?, max_sp=?,
                    gold=?, stat_points=?, skill_points=?, pk_points=?, reputation=?, last_online=NOW() WHERE id = ? AND account_id = ?`,
                [Math.max(1, int(c.Level, 1)), Math.max(0, num(c.Exp)), int(c.CurrentHp), int(c.CurrentMp), int(c.CurrentSp),
                    num(c.PosX), num(c.PosY), num(c.PosZ), num(c.RotationY), String(c.MapName || 'garner').slice(0, 64),
                    int(c.BaseStr), int(c.BaseAgi), int(c.BaseCon), int(c.BaseSpr), int(c.BaseSta), int(c.MaxHp), int(c.MaxMp), int(c.MaxSp),
                    Math.max(0, num(c.Gold)), int(c.StatPoints), int(c.SkillPoints), int(c.PkPoints), int(c.Reputation), id, req.user.id]);
            if (boats !== undefined)
                await conn.execute('UPDATE characters SET boats_json = ? WHERE id = ?', [JSON.stringify(boats), id]);
            if (questVersion === 1)
                await conn.execute('UPDATE characters SET quest_state_json = ?, job = ? WHERE id = ?', [JSON.stringify(c.OriginalQuests), c.Job, id]);
            if (gameplayVersion === 1)
                await conn.execute('UPDATE characters SET gameplay_state_json = ? WHERE id = ?', [JSON.stringify(c.Gameplay), id]);
            if (bankVersion === 1)
                await conn.execute('UPDATE characters SET bank_json = ? WHERE id = ?', [JSON.stringify(bank), id]);
            if (inv) {
                await conn.execute('DELETE FROM inventory WHERE character_id = ?', [id]);
                for (const i of inv) {
                    await conn.execute(
                        `INSERT INTO inventory (unique_item_id, character_id, slot_index, item_id, quantity, durability, fusion_item_id, medal_honor, medal_wins, medal_entries, medal_kills, medal_deaths, is_equipped, is_locked,
                            owner_character_id, refine_level, gem_slot_1, gem_slot_2, gem_slot_3)
                         VALUES (COALESCE(NULLIF(?, ''), UUID()), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                        [String(i.UniqueItemId || '').slice(0, 64), id, int(i.SlotIndex), int(i.ItemId), Math.max(1, int(i.Quantity, 1)), int(i.Durability, 100), int(i.FusionItemId), int(i.MedalHonor), int(i.MedalWins), int(i.MedalEntries), int(i.MedalKills), int(i.MedalDeaths),
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
            res.json({ success: true, revision: c.SaveRevision + 1, boatOwnershipVersion: 1, boatServicesVersion: 1, bankStorageVersion: bankVersion === 1 ? 1 : 0, questStateVersion: questVersion === 1 ? 1 : 0, gameplayStateVersion: gameplayVersion === 1 ? 1 : 0 });
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

    app.post(`${g}/mail/:charId/send`, wrap(async (req,res)=>res.json({success:false,error:'ORIGINAL_GM_MAIL_ONLY'})));
    app.post(`${g}/mail/:charId/:mailId/claim`, wrap(async (req,res)=>res.json({success:false,error:'ORIGINAL_GM_MAIL_ONLY'})));

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
    app.post(`${g}/stalls/:charId/buy`,wrap(async(req,res)=>{
        const id=int(req.params.charId), b=req.body || {};
        if (!validStallRequest(b) || id===b.sellerId) return res.json({success:false,error:'INVALID_STALL_REQUEST'});
        const hash=createHash('sha256').update(JSON.stringify(b)).digest('hex'),conn=await db.getConnection();
        try {
            await conn.beginTransaction();
            const [chars]=await conn.execute('SELECT id,account_id,is_deleted,save_revision,gold,gameplay_state_json FROM characters WHERE id IN (?,?) ORDER BY id FOR UPDATE',[id,b.sellerId]);
            const buyer=chars.find(c=>Number(c.id)===id),seller=chars.find(c=>Number(c.id)===b.sellerId);
            if (!buyer || Number(buyer.account_id)!==req.user.id) { await conn.rollback(); return res.json({success:false,error:'NOT_FOUND'}); }
            const [receipts]=await conn.execute('SELECT payload_hash,response_json FROM stall_purchase_receipts WHERE buyer_id=? AND operation_id=?',[id,b.operationId]);
            if (receipts.length) { await conn.rollback(); return res.json(receipts[0].payload_hash===hash?JSON.parse(receipts[0].response_json):{success:false,error:'OPERATION_MISMATCH'}); }
            const [items]=await conn.execute('SELECT * FROM inventory WHERE character_id=? AND unique_item_id=?',[b.sellerId,b.itemKey]);
            const [occupied]=await conn.execute('SELECT id FROM inventory WHERE character_id=? AND slot_index=?',[id,b.buyerSlot]);
            const sale=stallSale(b,{...buyer,id:Number(buyer.id),save_revision:Number(buyer.save_revision)},seller?{...seller,id:Number(seller.id),save_revision:Number(seller.save_revision)}:null,items[0],JSON.parse(seller?.gameplay_state_json || '{}'),occupied.length>0);
            await conn.execute('INSERT INTO stall_purchase_receipts (buyer_id,operation_id,payload_hash,response_json) VALUES (?,?,?,?)',[id,b.operationId,hash,JSON.stringify(sale.response)]);
            for (const [charId,revision] of [[id,b.buyerRevision],[b.sellerId,b.sellerRevision]]) await conn.execute('INSERT INTO character_save_receipts (character_id,operation_id,payload_hash,expected_revision,quest_id) VALUES (?,?,?,?,0)',[charId,b.operationId,hash,revision]);
            if (items[0].quantity===b.quantity) await conn.execute('DELETE FROM inventory WHERE character_id=? AND unique_item_id=?',[b.sellerId,b.itemKey]);
            else await conn.execute('UPDATE inventory SET quantity=quantity-? WHERE character_id=? AND unique_item_id=?',[b.quantity,b.sellerId,b.itemKey]);
            await conn.execute('INSERT INTO inventory ('+STALL_COLS.join(',')+') VALUES ('+STALL_COLS.map(()=>'?').join(',')+')',STALL_COLS.map(k=>sale.moved[k]));
            await conn.execute('UPDATE characters SET gold=?,save_revision=save_revision+1 WHERE id=?',[sale.response.buyerGold,id]);
            await conn.execute('UPDATE characters SET gold=?,save_revision=save_revision+1,gameplay_state_json=? WHERE id=?',[sale.response.sellerGold,JSON.stringify(sale.state),b.sellerId]);
            await conn.commit(); return res.json(sale.response);
        } catch(e) { await conn.rollback(); return res.json({success:false,error:['SAVE_CONFLICT','INVALID_OFFER','INSUFFICIENT_GOLD'].includes(e.message)?e.message:'DB_ERROR'}); }
        finally { conn.release(); }
    }));

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

    app.post(`${g}/guild/:charId/create`, wrap(async (req,res) => res.json({success:false,error:"ATOMIC_SAVE_REQUIRED"})));

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

    app.post(`${g}/guild/:charId/leave`, wrap(async (req,res) => {
        const id = int(req.params.charId);
        if (!(await owned(id,req.user.id))) return res.json({success:false,error:'NOT_FOUND'});
        const conn=await db.getConnection();
        try {
            await conn.beginTransaction();
            const [membership]=await conn.execute('SELECT guild_id,rank_name FROM guild_members WHERE character_id=? FOR UPDATE',[id]);
            if (!membership.length) { await conn.rollback(); return res.json({success:false,error:'NOT_IN_GUILD'}); }
            const guild=membership[0].guild_id;
            if (membership[0].rank_name==='Lider') {
                const [others]=await conn.execute('SELECT character_id FROM guild_members WHERE guild_id=? AND character_id!=? ORDER BY joined_at LIMIT 1 FOR UPDATE',[guild,id]);
                if (others.length) {
                    await conn.execute('UPDATE guild_members SET rank_name="Lider" WHERE character_id=?',[others[0].character_id]);
                    await conn.execute('UPDATE guilds SET leader_character_id=? WHERE id=?',[others[0].character_id,guild]);
                } else await conn.execute('DELETE FROM guilds WHERE id=?',[guild]);
            }
            await conn.execute('DELETE FROM guild_members WHERE character_id=?',[id]);
            await conn.commit(); return res.json({success:true});
        } catch(e) { await conn.rollback(); throw e; } finally { conn.release(); }
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

    app.post(`${g}/guild/:charId/rank`, wrap(async (req,res) => {
        const id = int(req.params.charId), b = req.body || {};
        if (!(await owned(id,req.user.id))) return res.json({success:false,error:'NOT_FOUND'});
        const [result] = await db.execute('UPDATE guild_members target JOIN guild_members leader ON target.guild_id = leader.guild_id SET target.rank_name = ? WHERE leader.character_id = ? AND leader.rank_name = "Lider" AND target.character_name = ? AND target.rank_name != "Lider"', [b.officer === true ? 'Oficial':'Membro',id,String(b.targetName || '')]);
        res.json({success:result.affectedRows>0});
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
        const [objectives] = await db.execute('SELECT quest_id, objective_index, progress FROM quest_objective_progress WHERE character_id = ? ORDER BY objective_index', [charId]);
        res.json({
            success: true,
            active: rows.filter(r => !r.completed_at).map(r => {
                const extra = objectives.filter(o => o.quest_id === r.quest_id)
                    .map(o => ({ objectiveIndex: o.objective_index, progress: o.progress }));
                return { questId: r.quest_id, progress: r.progress, ...(extra.length ? { objectiveProgress: extra } : {}) };
            }),
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
            const index = b.objectiveIndex === undefined ? 0 : b.objectiveIndex;
            if (action === 'progress' && (!questNumber(index, 0) || index > 15))
                return res.json({ success: false, error: 'INVALID_OBJECTIVE' });
            let result;
            if (action === 'accept') {
                [result] = await db.execute('INSERT IGNORE INTO quest_progress (character_id, quest_id, progress) VALUES (?, ?, 0)', [charId, b.questId]);
            } else if (action === 'abandon') {
                [result] = await db.execute('DELETE FROM quest_progress WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL', [charId, b.questId]);
            } else if (action === 'progress' && index > 0) {
                [result] = await db.execute(`INSERT INTO quest_objective_progress (character_id, quest_id, objective_index, progress)
                    SELECT character_id, quest_id, ?, ? FROM quest_progress
                    WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL
                    ON DUPLICATE KEY UPDATE progress = GREATEST(quest_objective_progress.progress, VALUES(progress))`,
                    [index, b.progress, charId, b.questId]);
                // MySQL can report zero affected rows for an already-confirmed monotonic retry.
                if (result.affectedRows === 0) {
                    const [active] = await db.execute('SELECT quest_id FROM quest_progress WHERE character_id = ? AND quest_id = ? AND completed_at IS NULL', [charId, b.questId]);
                    if (active.length) return res.json({ success: true });
                }
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
