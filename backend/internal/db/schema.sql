CREATE TABLE IF NOT EXISTS installations (
  id TEXT PRIMARY KEY,
  metadata_json TEXT NOT NULL DEFAULT '{}',
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sessions (
  id TEXT PRIMARY KEY,
  installation_id TEXT NOT NULL,
  game_version TEXT NOT NULL DEFAULT '',
  scenario TEXT NOT NULL DEFAULT 'night-1',
  state_json TEXT NOT NULL DEFAULT '{}',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (installation_id) REFERENCES installations(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_sessions_installation_id ON sessions(installation_id, updated_at);

CREATE TABLE IF NOT EXISTS messages (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT NOT NULL,
  role TEXT NOT NULL,
  content TEXT NOT NULL,
  provider TEXT NOT NULL DEFAULT '',
  model TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_messages_session_id ON messages(session_id, id);

CREATE TABLE IF NOT EXISTS events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT NOT NULL,
  client_seq INTEGER NOT NULL DEFAULT 0,
  event_type TEXT NOT NULL,
  payload_json TEXT NOT NULL DEFAULT '{}',
  created_at TEXT NOT NULL,
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_events_session_id ON events(session_id, id);

CREATE TABLE IF NOT EXISTS llm_requests (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  installation_id TEXT NOT NULL DEFAULT '',
  session_id TEXT NOT NULL DEFAULT '',
  request_mode TEXT NOT NULL DEFAULT 'backend_history',
  provider TEXT NOT NULL DEFAULT '',
  model TEXT NOT NULL DEFAULT '',
  endpoint TEXT NOT NULL DEFAULT '',
  prompt_tokens INTEGER NOT NULL DEFAULT 0,
  completion_tokens INTEGER NOT NULL DEFAULT 0,
  total_tokens INTEGER NOT NULL DEFAULT 0,
  latency_ms INTEGER NOT NULL DEFAULT 0,
  tokens_per_second REAL NOT NULL DEFAULT 0,
  time_to_first_token_ms INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT '',
  error_text TEXT NOT NULL DEFAULT '',
  user_message_id INTEGER NOT NULL DEFAULT 0,
  assistant_message_id INTEGER NOT NULL DEFAULT 0,
  user_message TEXT NOT NULL DEFAULT '',
  assistant_message TEXT NOT NULL DEFAULT '',
  request_messages_json TEXT NOT NULL DEFAULT '[]',
  history_count INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  FOREIGN KEY (installation_id) REFERENCES installations(id) ON DELETE CASCADE,
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_llm_requests_session_id ON llm_requests(session_id, id);
CREATE INDEX IF NOT EXISTS idx_llm_requests_installation_id ON llm_requests(installation_id, id);
CREATE INDEX IF NOT EXISTS idx_llm_requests_status_created_at ON llm_requests(status, created_at);
