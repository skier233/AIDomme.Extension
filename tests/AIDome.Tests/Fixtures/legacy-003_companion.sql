CREATE TABLE aidomme_guided_recovery (
  "OwnerId" integer PRIMARY KEY REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
  "Revision" bigint NOT NULL, "Payload" text NOT NULL, "UpdatedAtUtc" timestamptz NOT NULL,
  CHECK ("OwnerId" > 0));
CREATE TABLE aidomme_narration_receipts (
  "OwnerId" integer NOT NULL REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
  "MessageId" uuid NOT NULL, "ClaimedAtUtc" timestamptz NOT NULL,
  PRIMARY KEY ("OwnerId", "MessageId"));
CREATE TABLE aidomme_guided_policies (
  "OwnerId" integer NOT NULL, "SessionId" integer NOT NULL, "Revision" integer NOT NULL,
  "Policy" text NOT NULL, "AcceptedAtUtc" timestamptz NOT NULL,
  PRIMARY KEY ("OwnerId", "SessionId", "Revision"),
  FOREIGN KEY ("OwnerId", "SessionId") REFERENCES aidomme_guided_sessionrecord("OwnerId", "Id") ON DELETE CASCADE);
CREATE TABLE aidomme_guided_controls (
  "OwnerId" integer NOT NULL REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
  "EventId" uuid NOT NULL, "Payload" text NOT NULL, "Status" text NOT NULL,
  PRIMARY KEY ("OwnerId", "EventId"), CHECK ("Status" IN ('Pending', 'Applied', 'Uncertain')));
CREATE TABLE aidomme_guided_event_steps (
  "OwnerId" integer NOT NULL, "EventId" uuid NOT NULL, "StepNumber" integer NOT NULL,
  PRIMARY KEY ("OwnerId", "EventId"),
  FOREIGN KEY ("OwnerId", "EventId") REFERENCES aidomme_guided_guidedeventreceipt("OwnerId", "EventId") ON DELETE CASCADE);
