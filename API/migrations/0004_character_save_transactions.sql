ALTER TABLE characters ADD COLUMN IF NOT EXISTS save_revision BIGINT NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS character_save_receipts (
  character_id BIGINT NOT NULL,
  operation_id VARCHAR(36) NOT NULL,
  payload_hash CHAR(64) NOT NULL,
  expected_revision BIGINT NOT NULL,
  quest_id INT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (character_id, operation_id)
);
