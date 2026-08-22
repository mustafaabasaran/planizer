-- A GLOBAL temp table (##) carries no per-session suffix, so its name may use the full 128
-- characters; only local (#) temp table names are capped at 116. 120 characters below.
-- expect-none: MSSQL-LIM-002
CREATE TABLE ##GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG (Id int NOT NULL);
