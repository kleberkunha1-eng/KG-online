CREATE TABLE IF NOT EXISTS quest_objective_progress (
  character_id INTEGER NOT NULL,
  quest_id INTEGER NOT NULL,
  objective_index INTEGER NOT NULL CHECK (objective_index BETWEEN 1 AND 15),
  progress INTEGER NOT NULL DEFAULT 0 CHECK (progress BETWEEN 0 AND 2147483647),
  PRIMARY KEY (character_id, quest_id, objective_index),
  FOREIGN KEY (character_id, quest_id) REFERENCES quest_progress(character_id, quest_id) ON DELETE CASCADE
);
