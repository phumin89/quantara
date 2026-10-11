CREATE SCHEMA IF NOT EXISTS live;
CREATE TABLE IF NOT EXISTS live.accounts (
    id uuid PRIMARY KEY,
    revision bigint NOT NULL CHECK (revision > 0),
    document jsonb NOT NULL
);
-- Full version snapshots keep account money, orders and audit atomic.
-- ponytail: suitable for a small personal account; compact projections only after measured growth.
CREATE TABLE IF NOT EXISTS live.account_history (
    account_id uuid NOT NULL REFERENCES live.accounts(id),
    revision bigint NOT NULL CHECK (revision > 0),
    document jsonb NOT NULL,
    PRIMARY KEY (account_id, revision)
);
CREATE TABLE IF NOT EXISTS live.intent_evidence (
    account_id uuid NOT NULL REFERENCES live.accounts(id),
    intent_id uuid NOT NULL,
    evidence jsonb NOT NULL,
    PRIMARY KEY (account_id, intent_id)
);
CREATE TABLE IF NOT EXISTS live.fill_evidence (
    account_id uuid NOT NULL REFERENCES live.accounts(id),
    transaction_id text NOT NULL CHECK (length(transaction_id) > 0),
    revision bigint NOT NULL CHECK (revision > 0),
    evidence jsonb NOT NULL,
    PRIMARY KEY (account_id, transaction_id, revision)
);

CREATE OR REPLACE FUNCTION live.reject_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Live evidence is append-only' USING ERRCODE = '55000';
END;
$$;
CREATE OR REPLACE TRIGGER immutable_account_history BEFORE UPDATE OR DELETE OR TRUNCATE
ON live.account_history FOR EACH STATEMENT EXECUTE FUNCTION live.reject_history_mutation();
CREATE OR REPLACE TRIGGER immutable_intent_evidence BEFORE UPDATE OR DELETE OR TRUNCATE
ON live.intent_evidence FOR EACH STATEMENT EXECUTE FUNCTION live.reject_history_mutation();
CREATE OR REPLACE TRIGGER immutable_fill_evidence BEFORE UPDATE OR DELETE OR TRUNCATE
ON live.fill_evidence FOR EACH STATEMENT EXECUTE FUNCTION live.reject_history_mutation();

-- Observer starts before onboarding and never mutates the account ledger.
CREATE TABLE IF NOT EXISTS live.bitkub_order_receipts (
    account_id uuid NOT NULL,
    credential_fingerprint text NOT NULL,
    order_id text NOT NULL,
    updated_at bigint NOT NULL CHECK (updated_at > 0),
    client_id text NOT NULL,
    document jsonb NOT NULL,
    PRIMARY KEY (account_id, credential_fingerprint, order_id, updated_at)
);
CREATE OR REPLACE TRIGGER immutable_bitkub_receipts BEFORE UPDATE OR DELETE OR TRUNCATE
ON live.bitkub_order_receipts FOR EACH STATEMENT EXECUTE FUNCTION live.reject_history_mutation();
CREATE TABLE IF NOT EXISTS live.bitkub_receipt_conflicts (
    account_id uuid NOT NULL,
    credential_fingerprint text NOT NULL,
    order_id text NOT NULL,
    updated_at bigint NOT NULL,
    document jsonb NOT NULL,
    PRIMARY KEY (account_id, credential_fingerprint, order_id, updated_at)
);
CREATE OR REPLACE TRIGGER immutable_bitkub_receipt_conflicts BEFORE UPDATE OR DELETE OR TRUNCATE
ON live.bitkub_receipt_conflicts FOR EACH STATEMENT EXECUTE FUNCTION live.reject_history_mutation();

CREATE OR REPLACE FUNCTION live.record_account_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    item jsonb;
    previous jsonb;
    intent uuid;
    transaction_id_value text;
    revision_value bigint;
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF NEW.id <> OLD.id OR NEW.revision <> OLD.revision + 1 THEN
            RAISE EXCEPTION 'Invalid account revision' USING ERRCODE = '23514';
        END IF;
        IF NULLIF(OLD.document->'OpeningBalance', 'null'::jsonb) IS DISTINCT FROM
           NULLIF(NEW.document->'OpeningBalance', 'null'::jsonb) THEN
            RAISE EXCEPTION 'Opening wallet cannot be rewritten' USING ERRCODE = '23514';
        END IF;
        -- Expand each audit once; indexed extraction would copy the full new array per prior event.
        IF EXISTS (
            SELECT 1 FROM jsonb_array_elements(OLD.document->'Audit') WITH ORDINALITY a(value, ordinal)
            LEFT JOIN jsonb_array_elements(NEW.document->'Audit') WITH ORDINALITY b(value, ordinal)
                ON a.ordinal = b.ordinal
            WHERE a.value IS DISTINCT FROM b.value
        ) THEN
            RAISE EXCEPTION 'Live audit cannot be rewritten' USING ERRCODE = '23514';
        END IF;
    END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(NEW.document->'Orders') LOOP
        intent := (item->'Intent'->>'Id')::uuid;
        INSERT INTO live.intent_evidence(account_id, intent_id, evidence)
        VALUES (NEW.id, intent, item->'Intent') ON CONFLICT DO NOTHING;
        SELECT evidence INTO previous FROM live.intent_evidence WHERE account_id=NEW.id AND intent_id=intent;
        IF previous IS DISTINCT FROM item->'Intent' THEN
            RAISE EXCEPTION 'Intent identity conflict' USING ERRCODE = '23514';
        END IF;
    END LOOP;
    FOR item IN
        SELECT value FROM jsonb_array_elements(NEW.document->'Fills')
        UNION
        SELECT (value->>'Detail')::jsonb FROM jsonb_array_elements(NEW.document->'Audit')
        WHERE value->>'Kind' IN ('FillApplied', 'FeeCorrected')
    LOOP
        transaction_id_value := item->>'TransactionId';
        revision_value := (item->>'Revision')::bigint;
        INSERT INTO live.fill_evidence(account_id, transaction_id, revision, evidence)
        VALUES (NEW.id, transaction_id_value, revision_value, item) ON CONFLICT DO NOTHING;
        SELECT evidence INTO previous FROM live.fill_evidence
        WHERE account_id=NEW.id AND transaction_id=transaction_id_value AND revision=revision_value;
        IF previous IS DISTINCT FROM item THEN
            RAISE EXCEPTION 'Fill evidence conflict' USING ERRCODE = '23514';
        END IF;
    END LOOP;
    INSERT INTO live.account_history(account_id, revision, document) VALUES (NEW.id, NEW.revision, NEW.document);
    RETURN NEW;
END;
$$;
CREATE OR REPLACE TRIGGER account_evidence AFTER INSERT OR UPDATE ON live.accounts
FOR EACH ROW EXECUTE FUNCTION live.record_account_evidence();

-- Preserve pre-migration account state. Historical revisions cannot be invented.
INSERT INTO live.account_history(account_id, revision, document)
SELECT id, revision, document FROM live.accounts ON CONFLICT DO NOTHING;
