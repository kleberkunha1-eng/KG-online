CREATE TABLE IF NOT EXISTS quest_objective_progress (
  character_id BIGINT NOT NULL,
  quest_id INT NOT NULL,
  objective_index INT NOT NULL CHECK (objective_index BETWEEN 1 AND 15),
  progress INT NOT NULL DEFAULT 0 CHECK (progress >= 0),
  PRIMARY KEY (character_id, quest_id, objective_index),
  FOREIGN KEY (character_id, quest_id) REFERENCES quest_progress(character_id, quest_id) ON DELETE CASCADE
);
