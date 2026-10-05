CREATE TABLE IF NOT EXISTS crash_reports (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  launcher_version TEXT,
  game_version TEXT,
  exit_code INTEGER,
  exit_signal TEXT,
  os_info TEXT,
  log_text TEXT,
  ip_address TEXT,
  created_at INTEGER NOT NULL DEFAULT (unixepoch())
);
CREATE INDEX IF NOT EXISTS idx_crash_created ON crash_reports(created_at);
