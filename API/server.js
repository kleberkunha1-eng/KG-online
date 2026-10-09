const path = require('path');
const fs = require('fs');
require('dotenv').config({ path: [path.join(__dirname, '.env.local'), path.join(__dirname, '.env')] });
if (!process.env.JWT_SECRET || process.env.JWT_SECRET.length < 32) { console.error('JWT_SECRET ausente ou curto (min 32).'); process.exit(1); }
const express = require('express');
const mysql = require('mysql2/promise');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const helmet = require('helmet');
const cors = require('cors');
const rateLimit = require('express-rate-limit');
const trace = require('./trace');

const app = express();

// ============================================================
// MIDDLEWARES DE SEGURANCA
// ============================================================
app.use(trace.middleware);
app.use(helmet());
app.set('trust proxy', parseInt(process.env.TRUST_PROXY || '0', 10));
const allowed = (process.env.CORS_ORIGINS || '').split(',').map(s => s.trim()).filter(Boolean);
app.use(cors({ origin: (o, cb) => cb(null, !o || !allowed.length || allowed.includes(o)) }));
app.use(express.json({ limit: '64kb' }));

const authLimiter = rateLimit({
    windowMs: 15 * 60 * 1000,
    max: 10,
    message: { success: false, error: 'Muitas tentativas. Tente novamente em 15 minutos.' },
    standardHeaders: true,
    legacyHeaders: false,
});

// ============================================================
// POOL DE CONEXOES - top_unity
// ============================================================
const dbConfig = {
    host: process.env.DB_HOST || '127.0.0.1',
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASS || '123456',
    database: process.env.DB_NAME || 'top_unity',
    port: process.env.DB_PORT || 3306,
    waitForConnections: true,
    connectionLimit: 20,
    queueLimit: 0,
    enableKeepAlive: true,
    keepAliveInitialDelay: 0
};

console.log('[DB] Configuracao:');
console.log(`[DB]   Host: ${dbConfig.host}`);
console.log(`[DB]   User: ${dbConfig.user}`);
console.log(`[DB]   Database: ${dbConfig.database}`);
console.log(`[DB]   Port: ${dbConfig.port}`);

const dbPool = trace.instrumentPool(mysql.createPool(dbConfig));

// Helper: Log de seguranca
async function logSecurity(connection, accountId, type, description, ip) {
    try {
        await connection.execute(
            `INSERT INTO security_logs (account_id, log_type, description, ip_address) VALUES (?, ?, ?, ?)`,
            [accountId, type, description, ip]
        );
    } catch (e) {
        console.error('[SecurityLog] Falha:', e.message);
    }
}

// ============================================================
// SETUP INICIAL - Cria conta de teste se nao existir
// ============================================================
async function setupTestAccount() {
    let connection;
    try {
        console.log('[Setup] Tentando conectar ao MariaDB...');
        connection = await dbPool.getConnection();
        console.log('[Setup] Conectado ao MariaDB com sucesso!');

        // Verifica se tabela accounts existe
        const [tables] = await connection.execute(
            `SELECT 1 FROM information_schema.tables WHERE table_schema = ? AND table_name = 'accounts' LIMIT 1`,
            [dbConfig.database]
        );

        if (tables.length === 0) {
            console.error('[Setup] ERRO: Tabela accounts NAO EXISTE no banco top_unity!');
            console.error('[Setup] Execute o script SQL no HeidiSQL primeiro.');
            connection.release();
            return;
        }
        console.log('[Setup] Tabela accounts encontrada.');

        const [rows] = await connection.execute(
            `SELECT id FROM accounts WHERE username = ? LIMIT 1`,
            ['admin']
        );

        if (rows.length === 0) {
            const hash = await bcrypt.hash('123456', 12);
            await connection.execute(
                `INSERT INTO accounts (username, email, password_hash, created_at) VALUES (?, ?, ?, NOW())`,
                ['admin', 'admin@teste.com', hash]
            );
            console.log('[Setup] ==========================================');
            console.log('[Setup] Conta de teste criada com sucesso!');
            console.log('[Setup] Usuario: admin');
            console.log('[Setup] Senha: 123456');
            console.log('[Setup] ==========================================');
        } else {
            console.log('[Setup] Conta admin ja existe. Pronto para login.');
        }

        connection.release();
    } catch (err) {
        console.error('[Setup] ERRO ao conectar/criar conta teste:', err.message);
        console.error('[Setup] Verifique:');
        console.error('[Setup]   1. MariaDB esta rodando (HeidiSQL conecta?)');
        console.error('[Setup]   2. Senha do root esta correta no .env');
        console.error('[Setup]   3. Banco top_unity existe e tabelas foram criadas');
        if (connection) connection.release();
    }
}

// ============================================================
// ROTAS
// ============================================================

// Health Check
app.get('/api/health', (req, res) => {
    res.json({ status: 'API Online', db: 'top_unity', timestamp: new Date().toISOString() });
});

// REGISTRO
app.post('/api/auth/register', authLimiter, async (req, res) => {
    const ip = req.ip || req.connection.remoteAddress;
    const { username, password, email } = req.body;

    if (!username || !password || !email) {
        return res.status(400).json({ success: false, error: 'Dados incompletos.' });
    }
    if (!/^[a-zA-Z0-9_]{3,32}$/.test(username)) {
        return res.status(400).json({ success: false, error: 'Usuario invalido. Use 3-32 caracteres (letras, numeros, _).' });
    }
    if (password.length < 6 || password.length > 128) {
        return res.status(400).json({ success: false, error: 'Senha deve ter entre 6 e 128 caracteres.' });
    }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        return res.status(400).json({ success: false, error: 'Email invalido.' });
    }

    let connection;
    try {
        connection = await dbPool.getConnection();

        const [existing] = await connection.execute(
            `SELECT id FROM accounts WHERE username = ? OR email = ? LIMIT 1`,
            [username, email]
        );

        if (existing.length > 0) {
            await logSecurity(connection, null, 'REGISTER_FAIL', 'Username ou email ja existe', ip);
            return res.status(409).json({ success: false, error: 'Usuario ou email ja cadastrado.' });
        }

        const saltRounds = parseInt(process.env.BCRYPT_ROUNDS) || 12;
        const passwordHash = await bcrypt.hash(password, saltRounds);

        const [result] = await connection.execute(
            `INSERT INTO accounts (username, email, password_hash, created_at) VALUES (?, ?, ?, NOW())`,
            [username, email, passwordHash]
        );

        await logSecurity(connection, result.insertId, 'REGISTER_OK', `Conta criada: ${username}`, ip);

        res.status(201).json({
            success: true,
            message: 'Conta criada com sucesso!',
            accountId: result.insertId
        });

    } catch (err) {
        console.error('[Register] Erro:', err.message);
        res.status(500).json({ success: false, error: 'Erro interno do servidor.' });
    } finally {
        if (connection) connection.release();
    }
});

// LOGIN
app.post('/api/auth/login', authLimiter, async (req, res) => {
    const ip = req.ip || req.connection.remoteAddress;
    const { username, password } = req.body;

    if (!username || !password) {
        return res.status(400).json({ success: false, error: 'Usuario e senha obrigatorios.' });
    }

    let connection;
    try {
        connection = await dbPool.getConnection();

        const [rows] = await connection.execute(
            `SELECT id, username, password_hash, is_admin, is_banned, ban_until, failed_logins, locked_until 
             FROM accounts WHERE username = ? LIMIT 1`,
            [username]
        );

        if (rows.length === 0) {
            await logSecurity(connection, null, 'LOGIN_FAIL', 'Usuario nao encontrado', ip);
            return res.status(404).json({ success: false, error: 'Usuario nao encontrado.' });
        }

        const user = rows[0];

        if (user.is_banned && user.ban_until && new Date(user.ban_until) > new Date()) {
            await logSecurity(connection, user.id, 'LOGIN_BANNED', `Banido ate ${user.ban_until}`, ip);
            return res.status(403).json({ success: false, error: 'Conta suspensa.', banUntil: user.ban_until });
        }

        if (user.locked_until && new Date(user.locked_until) > new Date()) {
            return res.status(423).json({ success: false, error: 'Conta temporariamente bloqueada por tentativas excessivas.' });
        }

        const validPassword = await bcrypt.compare(password, user.password_hash);

        if (!validPassword) {
            const newFails = user.failed_logins + 1;
            const lockedUntil = newFails >= 5
                ? new Date(Date.now() + 30 * 60 * 1000)
                : null;

            await connection.execute(
                `UPDATE accounts SET failed_logins = ?, locked_until = ? WHERE id = ?`,
                [newFails, lockedUntil, user.id]
            );

            await logSecurity(connection, user.id, 'LOGIN_FAIL', `Senha incorreta (tentativa ${newFails})`, ip);
            return res.status(401).json({ success: false, error: 'Senha incorreta.' });
        }

        const token = jwt.sign(
            { sub: user.id, usr: user.username, adm: !!user.is_admin },
            process.env.JWT_SECRET,
            { expiresIn: process.env.JWT_EXPIRES_IN || '2h' }
        );

        await connection.execute(
            `UPDATE accounts 
             SET failed_logins = 0, 
                 locked_until = NULL, 
                 last_login = NOW(), 
                 last_login_ip = ?,
                 session_token = ?,
                 session_expires = DATE_ADD(NOW(), INTERVAL 24 HOUR)
             WHERE id = ?`,
            [ip, token, user.id]
        );

        await logSecurity(connection, user.id, 'LOGIN_OK', 'Login bem-sucedido', ip);

        res.json({
            success: true,
            token: token,
            accountId: user.id,
            username: user.username,
            isAdmin: !!user.is_admin
        });

    } catch (err) {
        console.error('[Login] Erro:', err.message);
        res.status(500).json({ success: false, error: 'Erro interno do servidor.' });
    } finally {
        if (connection) connection.release();
    }
});

// VERIFICAR TOKEN (usado pelo Mirror)
app.post('/api/auth/verify', async (req, res) => {
    const { token } = req.body;
    if (!token) return res.status(400).json({ valid: false });

    try {
        const decoded = jwt.verify(token, process.env.JWT_SECRET);

        const [rows] = await dbPool.execute(
            `SELECT id, is_banned FROM accounts WHERE id = ? AND session_token = ? AND session_expires > NOW() LIMIT 1`,
            [decoded.sub, token]
        );

        if (rows.length === 0 || rows[0].is_banned) {
            return res.json({ valid: false });
        }

        res.json({
            valid: true,
            accountId: decoded.sub,
            username: decoded.usr
        });

    } catch (err) {
        res.json({ valid: false, error: err.message });
    }
});

// ============================================================
// MIGRACAO DE SCHEMA (idempotente)
// ============================================================
async function migrate() {
    await dbPool.query('ALTER TABLE accounts ADD COLUMN IF NOT EXISTS is_admin TINYINT(1) NOT NULL DEFAULT 1');
    await dbPool.query('ALTER TABLE accounts MODIFY is_admin TINYINT(1) NOT NULL DEFAULT 0');
    await dbPool.query(`CREATE TABLE IF NOT EXISTS news (id INT AUTO_INCREMENT PRIMARY KEY, category VARCHAR(16) NOT NULL DEFAULT 'News', title VARCHAR(160) NOT NULL, excerpt VARCHAR(600) NOT NULL, created_at DATETIME DEFAULT CURRENT_TIMESTAMP)`);
    const [[n]] = await dbPool.query('SELECT COUNT(*) AS c FROM news');
    if (!n.c) await dbPool.query("INSERT INTO news (category, title, excerpt) VALUES ('News', 'Servidor aberto para testes', 'Crie sua conta, baixe o cliente e entre no mundo de GAME PROJECT - K/G.')");

    // ---- Sistemas sociais: amigos, correio, guildas ----
    await dbPool.query(`CREATE TABLE IF NOT EXISTS friendships (
        id INT AUTO_INCREMENT PRIMARY KEY,
        character_id BIGINT NOT NULL,
        friend_character_id BIGINT NOT NULL,
        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
        UNIQUE KEY uniq_pair (character_id, friend_character_id)
    )`);
    await dbPool.query(`CREATE TABLE IF NOT EXISTS mails (
        id INT AUTO_INCREMENT PRIMARY KEY,
        sender_id BIGINT NOT NULL,
        sender_name VARCHAR(32) NOT NULL,
        recipient_id BIGINT NOT NULL,
        subject VARCHAR(80) NOT NULL DEFAULT '',
        body VARCHAR(1000) NOT NULL DEFAULT '',
        gold BIGINT NOT NULL DEFAULT 0,
        item_id INT NOT NULL DEFAULT -1,
        item_quantity INT NOT NULL DEFAULT 0,
        item_refine INT NOT NULL DEFAULT 0,
        is_read TINYINT(1) NOT NULL DEFAULT 0,
        is_claimed TINYINT(1) NOT NULL DEFAULT 0,
        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
        INDEX idx_recipient (recipient_id)
    )`);
    await dbPool.query(`CREATE TABLE IF NOT EXISTS guilds (
        id INT AUTO_INCREMENT PRIMARY KEY,
        name VARCHAR(32) NOT NULL UNIQUE,
        leader_character_id BIGINT NOT NULL,
        notice VARCHAR(400) NOT NULL DEFAULT '',
        level INT NOT NULL DEFAULT 1,
        gold BIGINT NOT NULL DEFAULT 0,
        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    )`);
    await dbPool.query(`CREATE TABLE IF NOT EXISTS guild_members (
        id INT AUTO_INCREMENT PRIMARY KEY,
        guild_id INT NOT NULL,
        character_id BIGINT NOT NULL,
        character_name VARCHAR(32) NOT NULL,
        rank_name VARCHAR(16) NOT NULL DEFAULT 'Membro',
        joined_at DATETIME DEFAULT CURRENT_TIMESTAMP,
        UNIQUE KEY uniq_member (character_id)
    )`);

    // ---- Quests (progresso por personagem) ----
    await dbPool.query(`CREATE TABLE IF NOT EXISTS quest_progress (
        id INT AUTO_INCREMENT PRIMARY KEY,
        character_id BIGINT NOT NULL,
        quest_id INT NOT NULL,
        progress INT NOT NULL DEFAULT 0,
        completed_at DATETIME NULL,
        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
        UNIQUE KEY uniq_char_quest (character_id, quest_id),
        INDEX idx_char (character_id)
    )`);
    const transactionSchema = fs.readFileSync(path.join(__dirname, 'migrations', '0004_character_save_transactions.sql'), 'utf8');
    for (const sql of transactionSchema.split(';').map(statement => statement.trim()).filter(Boolean))
        await dbPool.query(sql);
}

require('./routes')(app, dbPool);
require('./portal')(app, dbPool);
require('./game')(app, dbPool);

// Site (build do Vite) servido pela mesma porta
const site = path.resolve(process.env.SITE_DIR || path.join(__dirname, 'site'));
app.use('/patch', express.static(path.join(__dirname, 'patch'), { setHeaders: r => r.setHeader('Cache-Control', 'no-cache') }));
app.use('/downloads', express.static(path.join(__dirname, 'downloads')));
app.use(express.static(site));
app.get(/^\/(?!api\/).*/, (req, res, next) => res.sendFile(path.join(site, 'index.html'), e => e && next()));

// ============================================================
// INICIALIZACAO
// ============================================================
const PORT = process.env.PORT || 3000;
const HOST = process.env.HOST || '0.0.0.0';
app.listen(PORT, HOST, async () => {
    console.log(`[AuthAPI] ==========================================`);
    console.log(`[AuthAPI] Servidor rodando na porta ${PORT}`);
    console.log(`[AuthAPI] Banco: top_unity`);
    console.log(`[AuthAPI] ==========================================`);

    await migrate();
    if (process.env.CREATE_TEST_ACCOUNT === '1') await setupTestAccount();
});
