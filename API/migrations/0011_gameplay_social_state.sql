ALTER TABLE characters ADD COLUMN IF NOT EXISTS gameplay_state_json LONGTEXT NOT NULL DEFAULT '{"Version":1,"Fairies":[],"Offers":[],"StallName":""}';
CREATE TABLE IF NOT EXISTS stall_purchase_receipts (buyer_id BIGINT NOT NULL, operation_id VARCHAR(36) NOT NULL, payload_hash CHAR(64) NOT NULL, response_json LONGTEXT NOT NULL, PRIMARY KEY (buyer_id,operation_id));
