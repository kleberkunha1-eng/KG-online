CREATE TABLE IF NOT EXISTS accounts (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT NOT NULL UNIQUE COLLATE NOCASE,
  email TEXT NOT NULL UNIQUE COLLATE NOCASE,
  password_hash TEXT NOT NULL,
  is_admin INTEGER NOT NULL DEFAULT 0,
  is_banned INTEGER NOT NULL DEFAULT 0,
  ban_until INTEGER,
  failed_logins INTEGER NOT NULL DEFAULT 0,
  locked_until INTEGER,
  session_token TEXT,
  session_expires INTEGER,
  last_login INTEGER,
  last_login_ip TEXT,
  created_at INTEGER NOT NULL DEFAULT (unixepoch())
);

CREATE TABLE IF NOT EXISTS characters (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id INTEGER NOT NULL REFERENCES accounts(id),
  slot_index INTEGER NOT NULL,
  name TEXT NOT NULL,
  gender INTEGER NOT NULL DEFAULT 0,
  job INTEGER NOT NULL DEFAULT 0,
  level INTEGER NOT NULL DEFAULT 1,
  exp INTEGER NOT NULL DEFAULT 0,
  base_str INTEGER NOT NULL DEFAULT 10, base_agi INTEGER NOT NULL DEFAULT 10, base_con INTEGER NOT NULL DEFAULT 10,
  base_spr INTEGER NOT NULL DEFAULT 10, base_sta INTEGER NOT NULL DEFAULT 10,
  max_hp INTEGER NOT NULL DEFAULT 100, max_mp INTEGER NOT NULL DEFAULT 50, max_sp INTEGER NOT NULL DEFAULT 100,
  current_hp INTEGER NOT NULL DEFAULT 100, current_mp INTEGER NOT NULL DEFAULT 50, current_sp INTEGER NOT NULL DEFAULT 100,
  stat_points INTEGER NOT NULL DEFAULT 0, skill_points INTEGER NOT NULL DEFAULT 0,
  gold INTEGER NOT NULL DEFAULT 0, bank_gold INTEGER NOT NULL DEFAULT 0,
  map_name TEXT NOT NULL DEFAULT 'garner',
  pos_x REAL NOT NULL DEFAULT 0, pos_y REAL NOT NULL DEFAULT 1.5, pos_z REAL NOT NULL DEFAULT 0, rotation_y REAL NOT NULL DEFAULT 0,
  hair_style INTEGER NOT NULL DEFAULT 0, hair_color INTEGER NOT NULL DEFAULT 0, face_style INTEGER NOT NULL DEFAULT 0,
  pk_points INTEGER NOT NULL DEFAULT 0, reputation INTEGER NOT NULL DEFAULT 0,
  play_time_seconds INTEGER NOT NULL DEFAULT 0,
  is_online INTEGER NOT NULL DEFAULT 0, is_deleted INTEGER NOT NULL DEFAULT 0, deleted_at INTEGER,
  created_at INTEGER NOT NULL DEFAULT (unixepoch()),
  last_online INTEGER
);
CREATE INDEX IF NOT EXISTS idx_char_account ON characters(account_id, is_deleted);
CREATE UNIQUE INDEX IF NOT EXISTS uk_char_name ON characters(name) WHERE is_deleted = 0;
CREATE UNIQUE INDEX IF NOT EXISTS uk_char_slot ON characters(account_id, slot_index) WHERE is_deleted = 0;

CREATE TABLE IF NOT EXISTS inventory (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  unique_item_id TEXT NOT NULL UNIQUE,
  character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
  slot_index INTEGER NOT NULL,
  item_id INTEGER NOT NULL,
  quantity INTEGER NOT NULL DEFAULT 1,
  durability INTEGER DEFAULT 100,
  refine_level INTEGER DEFAULT 0,
  gem_slot_1 INTEGER, gem_slot_2 INTEGER, gem_slot_3 INTEGER,
  is_equipped INTEGER DEFAULT 0,
  is_locked INTEGER DEFAULT 0,
  owner_character_id INTEGER,
  UNIQUE (character_id, slot_index)
);

CREATE TABLE IF NOT EXISTS skills (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
  skill_id INTEGER NOT NULL,
  level INTEGER NOT NULL DEFAULT 1,
  exp INTEGER NOT NULL DEFAULT 0,
  UNIQUE (character_id, skill_id)
);

CREATE TABLE IF NOT EXISTS audit_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id INTEGER, character_id INTEGER,
  action_type TEXT, action_data TEXT, ip_address TEXT,
  created_at INTEGER NOT NULL DEFAULT (unixepoch())
);

CREATE TABLE IF NOT EXISTS security_logs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id INTEGER, log_type TEXT, description TEXT, ip_address TEXT,
  created_at INTEGER NOT NULL DEFAULT (unixepoch())
);
CREATE INDEX IF NOT EXISTS idx_sec_ip ON security_logs(ip_address, log_type, created_at);

CREATE TABLE IF NOT EXISTS news (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  category TEXT NOT NULL DEFAULT 'News',
  title TEXT NOT NULL,
  excerpt TEXT NOT NULL,
  created_at INTEGER NOT NULL DEFAULT (unixepoch())
);
INSERT INTO news (category, title, excerpt) SELECT 'News', 'Servidor aberto para testes', 'Crie sua conta, baixe o cliente e entre no mundo de GAME PROJECT - K/G.' WHERE NOT EXISTS (SELECT 1 FROM news);