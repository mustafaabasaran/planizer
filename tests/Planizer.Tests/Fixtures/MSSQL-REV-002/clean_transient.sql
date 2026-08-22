-- planizer-test: rollback=true
-- DML against table variables and temp tables moves no persistent data: nothing to roll back.
-- expect-none: MSSQL-REV-002
DECLARE @Ids TABLE (Id bigint NOT NULL);
INSERT INTO @Ids (Id) VALUES (1);
UPDATE @Ids SET Id = 2;
DELETE FROM @Ids;
SELECT Id INTO #Stage FROM @Ids;
INSERT INTO #Stage (Id) VALUES (3);
DELETE FROM #Stage;
CREATE TABLE #tmp (Id int NOT NULL);
DROP TABLE #tmp;
