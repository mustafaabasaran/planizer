using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Message- and target-level regressions found while sweeping the <c>samples/</c> migrations
/// end-to-end (Task 13). Fixtures only assert rule/severity/line, so exact-wording and
/// lock-target checks live here.
/// </summary>
public class RuleMessageRegressionTests
{
    private static Report Analyze(string sql, PlanizerConfig? config = null)
        => new MsSqlAnalyzer().Analyze([("m.sql", sql)], config ?? new PlanizerConfig { Rollback = true });

    [Fact]
    public void Row_width_message_uses_singular_byte_for_one_byte_columns()
    {
        var report = Analyze("ALTER TABLE dbo.Orders ADD Flag tinyint NULL;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-016");
        Assert.Contains("grows each row by 1 byte toward", finding.Message);
    }

    [Fact]
    public void Row_width_message_keeps_plural_bytes_for_wider_columns()
    {
        var report = Analyze("ALTER TABLE dbo.Orders ADD Total money NULL;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-016");
        Assert.Contains("grows each row by 8 bytes toward", finding.Message);
    }

    [Fact]
    public void Sp_rename_column_lock_finding_names_the_table_not_the_column()
    {
        // The Sch-M lock of a column rename is taken on the table; the finding must not
        // present "dbo.Customers.Fax" as if it were the locked object.
        var report = Analyze("EXEC sp_rename 'dbo.Customers.Fax', 'Fax_deprecated', 'COLUMN';");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-001");
        Assert.Contains("on dbo.Customers;", finding.Message);
        Assert.DoesNotContain("dbo.Customers.Fax", finding.Message);
    }

    [Fact]
    public void Sp_rename_table_lock_finding_keeps_the_full_object_name()
    {
        var report = Analyze("EXEC sp_rename 'dbo.OldName', 'NewName';");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-001");
        Assert.Contains("on dbo.OldName;", finding.Message);
    }

    [Fact]
    public void Column_renames_on_the_same_table_are_not_cross_table_deadlock_potential()
    {
        const string sql = """
            BEGIN TRAN;
            EXEC sp_rename 'dbo.Customers.Fax', 'Fax_deprecated', 'COLUMN';
            EXEC sp_rename 'dbo.Customers.Telex', 'Telex_deprecated', 'COLUMN';
            COMMIT;
            """;

        var report = Analyze(sql);

        // Both renames lock dbo.Customers: one table, no cross-table deadlock potential.
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-LOCK-008");
    }

    [Fact]
    public void Missing_rollback_message_does_not_repeat_itself()
    {
        var report = Analyze("UPDATE dbo.T SET C = 1 WHERE Id = 1;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-REV-002");
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(
            "1 data-modification statement in this file has no automatic inverse (UPDATE\u00d71); the rollback script is incomplete \u2014 write the rollback by hand.",
            finding.Message);
    }

    [Fact]
    public void Clustered_index_with_drop_existing_gets_the_recreate_message_not_the_heap_claim()
    {
        var report = Analyze(
            "CREATE CLUSTERED INDEX IX_Orders_Id ON dbo.Orders (Id) WITH (DROP_EXISTING = ON);");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-013");
        Assert.Contains("DROP_EXISTING = ON", finding.Message);
        Assert.Contains("rebuilt only if the clustering key changes", finding.Message);
        Assert.DoesNotContain("only succeeds on a heap", finding.Message);
    }

    [Fact]
    public void Plain_clustered_index_create_keeps_the_heap_only_wording()
    {
        var report = Analyze("CREATE CLUSTERED INDEX IX_HeapTable_Id ON dbo.HeapTable (Id);");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-013");
        Assert.Contains("only succeeds on a heap", finding.Message);
    }

    [Fact]
    public void Statement_level_runtime_constant_default_is_metadata_only_on_enterprise()
    {
        var report = Analyze(
            "ALTER TABLE dbo.Orders ADD CreatedAt datetime2 NOT NULL DEFAULT GETDATE();",
            new PlanizerConfig { Edition = SqlEdition.Enterprise });

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-002");
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("runtime-constant default", finding.Message);
        Assert.Contains("metadata-only", finding.Message);
    }

    [Fact]
    public void Per_row_default_message_names_the_function_and_the_per_row_evaluation()
    {
        var report = Analyze(
            "ALTER TABLE dbo.Orders ADD RowGuid uniqueidentifier NOT NULL DEFAULT NEWID();",
            new PlanizerConfig { Edition = SqlEdition.Enterprise });

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-002");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("per-row default NEWID()", finding.Message);
    }

    [Fact]
    public void Suppressed_irreversible_findings_do_not_count_toward_the_summary()
    {
        const string sql = """
            -- planizer:ignore MSSQL-REV-001 table archived to cold storage (OPS-4711)
            DROP TABLE dbo.OrderAudit_Legacy;
            DROP TABLE dbo.StillCounts;
            """;

        var report = Analyze(sql);

        Assert.Equal(1, report.Summary.IrreversibleCount);
        Assert.Contains(report.Findings, f => f.RuleId == "MSSQL-REV-001" && f.Suppressed);
    }

    [Fact]
    public void Online_index_tuning_rules_stay_silent_where_online_cannot_run()
    {
        // Standard cannot run ONLINE = ON at all: LOCK-003 blocks the statement, and the
        // LOCK-004/005 tuning suggestions must not contradict that Blocker.
        var report = Analyze(
            "ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);",
            new PlanizerConfig { Edition = SqlEdition.Express });

        Assert.Contains(report.Findings, f => f.RuleId == "MSSQL-LOCK-003");
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "MSSQL-LOCK-004" or "MSSQL-LOCK-005");
    }

    [Fact]
    public void Plain_index_rebuild_and_reorganize_produce_no_missing_rollback_noise()
    {
        const string sql = """
            ALTER INDEX IX_Orders_CustomerId ON dbo.Orders REBUILD;
            ALTER INDEX ALL ON dbo.OrderLines REORGANIZE;
            """;

        var report = Analyze(sql);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-REV-002");
        Assert.True(report.Summary.RollbackComplete);
    }

    // --- corpus re-scan after nested-statement flattening (EF Core idempotent scripts) ---

    [Fact]
    public void Bracketed_sp_rename_target_and_plain_alter_table_are_the_same_table()
    {
        // EF Core writes ALTER TABLE [T] and sp_rename N'[T].[C]'; [T], T and dbo.T are one table.
        const string sql = """
            BEGIN TRAN;
            ALTER TABLE [BatchDraft] ADD [HasError] bit NULL;
            EXEC sp_rename N'[BatchDraft].[IsError]', N'HadError', N'COLUMN';
            ALTER TABLE dbo.BatchDraft ADD [Note] nvarchar(10) NULL;
            COMMIT;
            """;

        var report = Analyze(sql);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-LOCK-008");
        var rename = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-001" && f.Location.Line == 3);
        Assert.Contains("on BatchDraft;", rename.Message);
        Assert.DoesNotContain("[BatchDraft]", rename.Message);
    }

    [Fact]
    public void Same_table_name_in_two_schemas_is_still_cross_table_deadlock_potential()
    {
        const string sql = """
            BEGIN TRAN;
            ALTER TABLE audit.T ADD C1 int NULL;
            ALTER TABLE dbo.T ADD C2 int NULL;
            COMMIT;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-008");
        Assert.Contains("(audit.T, dbo.T)", finding.Message);
    }

    [Fact]
    public void Drop_index_created_nonclustered_earlier_in_the_file_is_not_a_rewrite()
    {
        const string sql = """
            CREATE INDEX [IX_Orders_Code] ON [Orders] ([Code]);
            GO
            DROP INDEX [IX_Orders_Code] ON [dbo].[Orders];
            """;

        var report = Analyze(sql);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-RW-013");
    }

    [Fact]
    public void Drop_index_created_clustered_earlier_in_the_file_is_a_certain_rewrite()
    {
        const string sql = """
            CREATE CLUSTERED INDEX IX_Heap_Id ON dbo.Heap (Id);
            GO
            DROP INDEX IX_Heap_Id ON dbo.Heap;
            """;

        var report = Analyze(sql);

        var drop = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-013" && f.Location.Line == 3);
        Assert.Equal(Severity.Critical, drop.Severity);
        Assert.False(drop.Inconclusive);
        Assert.Contains("created earlier in this file", drop.Message);
    }

    [Fact]
    public void Drop_index_created_only_later_in_the_file_stays_inconclusive()
    {
        const string sql = """
            DROP INDEX IX_Orders_Code ON dbo.Orders;
            GO
            CREATE NONCLUSTERED INDEX IX_Orders_Code ON dbo.Orders (Code);
            """;

        var report = Analyze(sql);

        var drop = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-013");
        Assert.True(drop.Inconclusive);
    }

    [Fact]
    public void Aliased_update_target_is_resolved_through_the_from_clause()
    {
        var report = Analyze("UPDATE T SET DefinitionType = 1 FROM [dbo].[ChargePackage] T;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-009");
        Assert.Contains("UPDATE on dbo.ChargePackage has no WHERE", finding.Message);
    }

    [Fact]
    public void Aliased_delete_of_a_temp_table_is_transient()
    {
        const string sql = """
            CREATE TABLE #tmp (Id int NOT NULL);
            DELETE T FROM #tmp T;
            """;

        var report = Analyze(sql);

        Assert.DoesNotContain(report.Findings,
            f => f.RuleId is "MSSQL-LOCK-009" or "MSSQL-REV-001" or "MSSQL-REV-002");
    }
}
