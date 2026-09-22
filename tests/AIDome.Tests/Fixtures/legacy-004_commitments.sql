CREATE TABLE aidomme_commitment_state (
  "OwnerId" integer PRIMARY KEY REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
  "Revision" bigint NOT NULL, "Payload" text NOT NULL, "UpdatedAtUtc" timestamptz NOT NULL,
  CHECK ("OwnerId" > 0));
