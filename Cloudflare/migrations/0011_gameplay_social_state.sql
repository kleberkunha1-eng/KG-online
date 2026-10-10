ALTER TABLE characters ADD COLUMN gameplay_state_json TEXT NOT NULL DEFAULT '{"Version":1,"Fairies":[],"Offers":[],"StallName":""}';
CREATE TABLE IF NOT EXISTS guilds (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, leader_character_id INTEGER NOT NULL, notice TEXT NOT NULL DEFAULT '', level INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS guild_members (id INTEGER PRIMARY KEY AUTOINCREMENT, guild_id INTEGER NOT NULL, character_id INTEGER NOT NULL UNIQUE, character_name TEXT NOT NULL, rank_name TEXT NOT NULL DEFAULT 'Membro', joined_at INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS stall_purchase_receipts (buyer_id INTEGER NOT NULL, operation_id TEXT NOT NULL, payload_hash TEXT NOT NULL, response_json TEXT NOT NULL, PRIMARY KEY (buyer_id,operation_id));
