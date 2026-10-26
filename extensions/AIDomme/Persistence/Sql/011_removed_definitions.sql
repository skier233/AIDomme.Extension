-- 011_removed_definitions: a method or punishment ending the user has removed from their library.
-- Removing never deletes: recorded edges and comparisons still point at it, and it can be restored.
-- A shared default is removed for one account through that account's override row.
ALTER TABLE aidomme_methods ADD COLUMN "Removed" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE aidomme_enders ADD COLUMN "Removed" boolean NOT NULL DEFAULT FALSE;
