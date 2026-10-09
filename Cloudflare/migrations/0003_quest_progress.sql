CREATE TABLE IF NOT EXISTS quest_progress (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
  quest_id INTEGER NOT NULL CHECK (quest_id > 0),
  progress INTEGER NOT NULL DEFAULT 0 CHECK (progress >= 0),
  completed_at INTEGER,
  created_at INTEGER NOT NULL DEFAULT (unixepoch()),
  UNIQUE (character_id, quest_id)
);
