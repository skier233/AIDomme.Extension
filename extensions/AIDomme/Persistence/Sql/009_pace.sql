-- 009_pace: minutes of actual play per session, so her budget can learn his pace (points per
-- minute) and a session length means the minutes he set for it.
ALTER TABLE aidomme_sessions ADD COLUMN "ActiveSeconds" integer;
