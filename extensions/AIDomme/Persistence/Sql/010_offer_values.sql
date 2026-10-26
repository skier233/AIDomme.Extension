-- 010_offer_values: an edge owed for later was valued at 1.5 session points, but an edge in a
-- session is worth about 3 to 4 (the edge itself plus the minute it takes). She offered twenty-odd
-- owed edges for small mercies and he then paid them off in-session, far past the budget. The
-- shared default is corrected where it still has its original value; personal overrides are the
-- user's own and stay as they are.
UPDATE aidomme_offer_definitions
   SET "PointsPerUnit" = 4, "MinAmount" = 3, "MaxAmount" = 15
 WHERE "Id" = 8 AND "UserId" IS NULL AND "Kind" = 'EdgesLater' AND "PointsPerUnit" = 1.5;
