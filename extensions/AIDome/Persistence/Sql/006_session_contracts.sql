-- 006_session_contracts: the accepted session contract replaces the earlier "policy".
-- The session row records its mode and contract; every accepted revision is kept alongside.
ALTER TABLE aidomme_sessions ADD COLUMN "Mode" varchar(32), ADD COLUMN "ContractJson" text;
ALTER TABLE aidomme_session_policies RENAME TO aidomme_session_contracts;
ALTER TABLE aidomme_session_contracts RENAME COLUMN "Policy" TO "Contract";
