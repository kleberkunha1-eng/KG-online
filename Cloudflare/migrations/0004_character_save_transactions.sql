ALTER TABLE characters ADD COLUMN save_revision INTEGER NOT NULL DEFAULT 0;

CREATE TABLE character_save_receipts (
  character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
  operation_id TEXT NOT NULL,
  payload_hash TEXT NOT NULL,
  expected_revision INTEGER NOT NULL CHECK (expected_revision >= 0),
  quest_id INTEGER NOT NULL DEFAULT 0,
  created_at INTEGER NOT NULL DEFAULT (unixepoch()),
  PRIMARY KEY (character_id, operation_id)
);

CREATE TRIGGER validate_character_save_receipt
BEFORE INSERT ON character_save_receipts
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM characters
    WHERE id = NEW.character_id AND is_deleted = 0 AND save_revision = NEW.expected_revision
  ) THEN RAISE(ABORT, 'SAVE_CONFLICT') END;
  SELECT CASE WHEN NEW.quest_id > 0 AND NOT EXISTS (
    SELECT 1 FROM quest_progress
    WHERE character_id = NEW.character_id AND quest_id = NEW.quest_id AND completed_at IS NULL
  ) THEN RAISE(ABORT, 'QUEST_NOT_ACTIVE') END;
END;
