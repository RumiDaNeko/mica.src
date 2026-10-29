ALTER TABLE requests ADD COLUMN code_hash TEXT;
ALTER TABLE requests ADD COLUMN form_hash TEXT;
ALTER TABLE requests ADD COLUMN attempts INTEGER NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS requests_form ON requests(form_hash);
