using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

public class DdlOperationClassifierTests
{
    private static readonly DdlBehaviorCatalog Catalog = DdlBehaviorCatalog.Load();

    [Theory]
    [InlineData("ALTER TABLE dbo.T ADD C int NULL;", DdlOperationKeys.AddColumnNullable)]
    [InlineData("ALTER TABLE dbo.T ADD C int;", DdlOperationKeys.AddColumnNullable)]
    [InlineData("ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;", DdlOperationKeys.AddColumnNotNullDefaultConst)]
    [InlineData("ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT (-1);", DdlOperationKeys.AddColumnNotNullDefaultConst)]
    [InlineData(
        "ALTER TABLE dbo.T ADD C datetime2 NOT NULL DEFAULT CAST('2020-01-01' AS datetime2);",
        DdlOperationKeys.AddColumnNotNullDefaultConst)]
    [InlineData(
        "ALTER TABLE dbo.T ADD C uniqueidentifier NOT NULL DEFAULT NEWID();",
        DdlOperationKeys.AddColumnNotNullDefaultNondet)]
    [InlineData(
        "ALTER TABLE dbo.T ADD C uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID();",
        DdlOperationKeys.AddColumnNotNullDefaultNondet)]
    // Statement-level functions are runtime constants (evaluated once per statement,
    // regardless of determinism): they keep the metadata-only fast path.
    [InlineData(
        "ALTER TABLE dbo.T ADD C datetime2 NOT NULL DEFAULT SYSDATETIME();",
        DdlOperationKeys.AddColumnNotNullDefaultConst)]
    [InlineData(
        "ALTER TABLE dbo.T ADD C datetime2 NOT NULL DEFAULT GETDATE();",
        DdlOperationKeys.AddColumnNotNullDefaultConst)]
    [InlineData(
        "ALTER TABLE dbo.T ADD C datetime2 NOT NULL DEFAULT CURRENT_TIMESTAMP;",
        DdlOperationKeys.AddColumnNotNullDefaultConst)]
    // Unrecognized functions stay conservative: treated as breaking the fast path.
    [InlineData(
        "ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT dbo.MyFunc();",
        DdlOperationKeys.AddColumnNotNullDefaultNondet)]
    [InlineData("ALTER TABLE dbo.T ADD C int NOT NULL;", DdlOperationKeys.AddColumnNotNullNoDefault)]
    [InlineData("ALTER TABLE dbo.T ADD Total AS (A + B) PERSISTED;", DdlOperationKeys.AddComputedPersisted)]
    [InlineData("ALTER TABLE dbo.T ADD Total AS (A + B);", DdlOperationKeys.AddColumnNullable)]
    [InlineData("ALTER TABLE dbo.T ADD CONSTRAINT CK_T CHECK (C > 0);", DdlOperationKeys.AddCheckOrFk)]
    [InlineData(
        "ALTER TABLE dbo.T ADD CONSTRAINT FK_T FOREIGN KEY (C) REFERENCES dbo.P (Id);",
        DdlOperationKeys.AddCheckOrFk)]
    [InlineData("ALTER TABLE dbo.T ADD CONSTRAINT PK_T PRIMARY KEY (Id);", DdlOperationKeys.AddPkOrUnique)]
    [InlineData("ALTER TABLE dbo.T ADD CONSTRAINT UQ_T UNIQUE (C);", DdlOperationKeys.AddPkOrUnique)]
    // Multi-element ADD: the riskiest element names the operation
    [InlineData("ALTER TABLE dbo.T ADD A int NULL, B int NOT NULL;", DdlOperationKeys.AddColumnNotNullNoDefault)]
    [InlineData("ALTER TABLE dbo.T DROP COLUMN C;", DdlOperationKeys.DropColumn)]
    [InlineData("ALTER TABLE dbo.T DROP COLUMN A, B;", DdlOperationKeys.DropColumn)]
    [InlineData("ALTER TABLE dbo.T SWITCH TO dbo.T2;", DdlOperationKeys.AlterTableSwitch)]
    [InlineData(
        "ALTER TABLE dbo.T REBUILD WITH (DATA_COMPRESSION = PAGE);",
        DdlOperationKeys.DataCompressionChange)]
    [InlineData("DROP TABLE dbo.T;", DdlOperationKeys.DropTable)]
    [InlineData("TRUNCATE TABLE dbo.T;", DdlOperationKeys.TruncateTable)]
    [InlineData("EXEC sp_rename 'dbo.T', 'T2';", DdlOperationKeys.SpRename)]
    [InlineData("ALTER TABLE dbo.T ADD DEFAULT 0 FOR C;", DdlOperationKeys.AddDefaultConstraint)]
    [InlineData("ALTER TABLE dbo.T ADD CONSTRAINT DF_T_C DEFAULT (0) FOR C;", DdlOperationKeys.AddDefaultConstraint)]
    [InlineData("ALTER TABLE dbo.T ENABLE TRIGGER trg_T;", DdlOperationKeys.EnableDisableTrigger)]
    [InlineData("ALTER TABLE dbo.T DISABLE TRIGGER ALL;", DdlOperationKeys.EnableDisableTrigger)]
    [InlineData("CREATE INDEX IX_T ON dbo.T (C);", DdlOperationKeys.CreateNonclusteredIndexOffline)]
    [InlineData("CREATE CLUSTERED INDEX CX ON dbo.T (Id);", DdlOperationKeys.CreateClusteredIndexOnHeap)]
    [InlineData(
        "CREATE INDEX IX_T ON dbo.T (C) WITH (ONLINE = ON);",
        DdlOperationKeys.CreateNonclusteredIndexOnline)]
    [InlineData(
        "CREATE CLUSTERED INDEX CX ON dbo.T (Id) WITH (ONLINE = ON);",
        DdlOperationKeys.CreateClusteredIndexOnline)]
    [InlineData("ALTER INDEX IX_T ON dbo.T REBUILD;", DdlOperationKeys.AlterIndexRebuildOffline)]
    [InlineData("ALTER INDEX IX_T ON dbo.T REBUILD WITH (ONLINE = ON);", DdlOperationKeys.AlterIndexRebuildOnline)]
    [InlineData(
        "ALTER INDEX IX_T ON dbo.T REBUILD WITH (DATA_COMPRESSION = ROW);",
        DdlOperationKeys.DataCompressionChange)]
    [InlineData("ALTER INDEX IX_T ON dbo.T REORGANIZE;", DdlOperationKeys.AlterIndexReorganize)]
    // ALTER COLUMN: the offline-certain kinds resolve; a bare type respecification cannot
    [InlineData("ALTER TABLE dbo.T ALTER COLUMN C int NULL;", DdlOperationKeys.AlterColumnNotNullToNull)]
    [InlineData("ALTER TABLE dbo.T ALTER COLUMN C int NOT NULL;", DdlOperationKeys.AlterColumnNullToNotNull)]
    [InlineData("ALTER TABLE dbo.T ALTER COLUMN C nvarchar(max);", DdlOperationKeys.AlterColumnWidenToMax)]
    [InlineData(
        "ALTER TABLE dbo.T ALTER COLUMN C nvarchar(100) COLLATE Latin1_General_CI_AS;",
        DdlOperationKeys.AlterColumnCollation)]
    // AST alone cannot name these: schema state or unmodeled operations
    [InlineData("ALTER TABLE dbo.T ALTER COLUMN C varchar(100);", null)]
    [InlineData("ALTER TABLE dbo.T DROP CONSTRAINT CK_T;", null)]
    [InlineData("ALTER TABLE dbo.T ADD Id int IDENTITY(1,1) NOT NULL;", null)]
    [InlineData("DROP INDEX IX_T ON dbo.T;", null)]
    [InlineData("SELECT 1;", null)]
    [InlineData("EXEC dbo.MyProc;", null)]
    public void Maps_statements_to_operation_keys(string sql, string? expectedKey)
    {
        Assert.Equal(expectedKey, DdlOperationClassifier.GetOperationKey(ParseSingle(sql).Ast));
    }

    [Theory]
    // Sch-M takers under the default target (2019 Standard): full or brief both count
    [InlineData("ALTER TABLE dbo.T ADD C int NULL;", true)]
    [InlineData("ALTER TABLE dbo.T ALTER COLUMN C int NULL;", true)]
    // Online rebuild has no Standard catalog row (LOCK-003 territory): judged by the
    // offline-equivalent row so the summary count does not shift with the edition.
    [InlineData("ALTER INDEX IX_T ON dbo.T REBUILD WITH (ONLINE = ON);", true)]
    // Offline-equivalent of an online nonclustered create is an S table lock, not Sch-M.
    [InlineData("CREATE INDEX IX_T ON dbo.T (C) WITH (ONLINE = ON);", false)]
    [InlineData("ALTER TABLE dbo.T SWITCH TO dbo.T2;", true)]
    [InlineData("DROP TABLE dbo.T;", true)]
    [InlineData("TRUNCATE TABLE dbo.T;", true)]
    [InlineData("EXEC sp_rename 'dbo.T', 'T2';", true)]
    [InlineData("CREATE CLUSTERED INDEX CX ON dbo.T (Id);", true)]
    [InlineData("ALTER INDEX IX_T ON dbo.T REBUILD;", true)]
    // No catalog row → conservative AST fallback: ALTER TABLE variants and DROP INDEX take Sch-M
    [InlineData("ALTER TABLE dbo.T ALTER COLUMN C varchar(100);", true)]
    [InlineData("ALTER TABLE dbo.T DROP CONSTRAINT CK_T;", true)]
    [InlineData("DROP INDEX IX_T ON dbo.T;", true)]
    // Not Sch-M: shared table lock, no lock, or plain DML
    [InlineData("CREATE INDEX IX_T ON dbo.T (C);", false)]
    [InlineData("ALTER INDEX IX_T ON dbo.T REORGANIZE;", false)]
    [InlineData("SELECT 1;", false)]
    [InlineData("EXEC dbo.MyProc;", false)]
    public void Classifies_schema_modification_locks(string sql, bool expected)
    {
        var statement = ParseSingle(sql);

        var acquiresSchM = DdlOperationClassifier.AcquiresSchMLock(statement, Catalog, new PlanizerConfig());

        Assert.Equal(expected, acquiresSchM);
    }

    [Fact]
    public void Behavior_resolution_is_edition_aware()
    {
        var statement = ParseSingle("ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;");

        var standard = DdlOperationClassifier.GetBehavior(statement, Catalog, new PlanizerConfig());
        var enterprise = DdlOperationClassifier.GetBehavior(
            statement, Catalog, new PlanizerConfig { Edition = SqlEdition.Enterprise });

        Assert.NotNull(standard);
        Assert.Equal(DataMovement.Rewrite, standard.Movement);
        Assert.NotNull(enterprise);
        Assert.Equal(DataMovement.MetadataOnly, enterprise.Movement);
    }

    private static SqlStatementInfo ParseSingle(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return result.Statements.Single();
    }
}
