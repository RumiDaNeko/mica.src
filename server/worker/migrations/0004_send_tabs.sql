CREATE TABLE IF NOT EXISTS devices(user_id TEXT NOT NULL REFERENCES users(id), id TEXT NOT NULL, name TEXT NOT NULL, seen INTEGER NOT NULL, PRIMARY KEY(user_id,id));
CREATE TABLE IF NOT EXISTS sent_tabs(id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), to_device TEXT NOT NULL, from_name TEXT NOT NULL, url TEXT NOT NULL, title TEXT NOT NULL, created INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS sent_tabs_device ON sent_tabs(user_id,to_device);
