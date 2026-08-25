using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Online index lock semantics, per Microsoft's "How online index operations work" phase table:
/// the preparation phase always takes a shared (S) lock on the table; the final phase takes S
/// again for a nonclustered CREATE, and a schema-modification (Sch-M) lock when a clustered index
/// is created or dropped online or when any index is rebuilt. The Sch-M an online build holds for
/// its whole duration is an object lock of resource subtype INDEX_OPERATION — it blocks concurrent
/// DDL, not DML, so it must never feed a "blocks all access" count.
/// </summary>
public class OnlineIndexLockTests
{
    private static readonly DdlBehaviorCatalog Catalog = DdlBehaviorCatalog.Load();

    private static readonly PlanizerConfig Enterprise2022 = new()
    {
        TargetVersion = SqlServerVersion.Sql2022,
        Edition = SqlEdition.Enterprise,
    };

    private static Report Analyze(string sql, PlanizerConfig config)
        => new MsSqlAnalyzer().Analyze([("m.sql", sql)], config);

    // --- MSSQL-LOCK-004 message is operation-aware ---

    [Fact]
    public void Online_nonclustered_create_message_names_only_shared_locks()
    {
        var report = Analyze("CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);", Enterprise2022);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-004");
        Assert.Equal(
            "Online CREATE INDEX still needs a brief shared (S) lock on dbo.T to start and again to "
            + "complete; without WAIT_AT_LOW_PRIORITY they queue at normal priority and can convoy "
            + "blocked sessions.",
            finding.Message);
        Assert.DoesNotContain("Sch-M", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Online_clustered_create_message_names_the_final_phase_sch_m()
    {
        var report = Analyze("CREATE CLUSTERED INDEX CX ON dbo.T (Id) WITH (ONLINE = ON);", Enterprise2022);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-004");
        Assert.Equal(
            "Online CREATE INDEX still needs a brief shared (S) lock on dbo.T to start and a "
            + "schema-modification (Sch-M) lock to complete; without WAIT_AT_LOW_PRIORITY they queue "
            + "at normal priority and can convoy blocked sessions.",
            finding.Message);
    }

    [Fact]
    public void Online_rebuild_message_names_the_final_phase_sch_m()
    {
        var report = Analyze(
            "ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);",
            new PlanizerConfig { Edition = SqlEdition.Enterprise });

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-004");
        Assert.Equal(
            "Online ALTER INDEX REBUILD still needs a brief shared (S) lock on dbo.T to start and a "
            + "schema-modification (Sch-M) lock to complete; without WAIT_AT_LOW_PRIORITY they queue "
            + "at normal priority and can convoy blocked sessions.",
            finding.Message);
    }

    [Fact]
    public void No_online_index_message_claims_a_sch_m_lock_at_the_start()
    {
        const string sql = """
            CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
            CREATE CLUSTERED INDEX CX ON dbo.T2 (Id) WITH (ONLINE = ON);
            ALTER INDEX IX ON dbo.T3 REBUILD WITH (ONLINE = ON);
            """;

        var report = Analyze(sql, Enterprise2022);

        foreach (var finding in report.Findings.Where(f => f.RuleId == "MSSQL-LOCK-004"))
        {
            Assert.Contains("shared (S) lock on", finding.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Sch-M lock to start", finding.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("at the start and end", finding.Message, StringComparison.Ordinal);
        }
    }

    // --- catalog: online creates are two rows, not one ---

    [Theory]
    [InlineData("CREATE INDEX IX_T ON dbo.T (C) WITH (ONLINE = ON);",
        DdlOperationKeys.CreateNonclusteredIndexOnline, LockLevel.SBrief)]
    [InlineData("CREATE CLUSTERED INDEX CX ON dbo.T (Id) WITH (ONLINE = ON);",
        DdlOperationKeys.CreateClusteredIndexOnline, LockLevel.SchMBrief)]
    public void Online_create_resolves_to_a_clustering_specific_row(
        string sql, string expectedKey, LockLevel expectedLock)
    {
        var statement = ParseSingle(sql);

        Assert.Equal(expectedKey, DdlOperationClassifier.GetOperationKey(statement.Ast));

        var behavior = DdlOperationClassifier.GetBehavior(statement, Catalog, Enterprise2022);
        Assert.NotNull(behavior);
        Assert.Equal(expectedLock, behavior.Lock);
        Assert.Equal(DataMovement.IndexBuild, behavior.Movement);
    }

    [Fact]
    public void S_brief_is_a_distinct_lock_token_and_is_not_a_sch_m()
    {
        var catalog = DdlBehaviorCatalog.Parse(
            "operation_key,edition,min_version,lock,data_movement,reversible,notes\n"
            + "probe,any,any,s_brief,index_build,yes,\n");

        var behavior = catalog.Lookup("probe", SqlServerVersion.Sql2019, SqlEdition.Standard);

        Assert.NotNull(behavior);
        Assert.Equal(LockLevel.SBrief, behavior.Lock);
        Assert.NotEqual(LockLevel.STable, behavior.Lock);
        Assert.NotEqual(LockLevel.SchMBrief, behavior.Lock);
    }

    // --- ScriptSummary must not count an online nonclustered create as a Sch-M taker ---

    [Fact]
    public void Online_nonclustered_create_does_not_count_toward_the_sch_m_summary()
    {
        var statement = ParseSingle("CREATE INDEX IX_T ON dbo.T (C) WITH (ONLINE = ON);");

        Assert.False(DdlOperationClassifier.AcquiresSchMLock(statement, Catalog, Enterprise2022));

        var report = Analyze("CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);", Enterprise2022);
        Assert.Equal(0, report.Summary.SchMLockCount);
    }

    [Theory]
    [InlineData("CREATE CLUSTERED INDEX CX ON dbo.T (Id) WITH (ONLINE = ON);")]
    [InlineData("ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);")]
    public void Online_operations_that_end_on_sch_m_still_count(string sql)
    {
        Assert.True(DdlOperationClassifier.AcquiresSchMLock(ParseSingle(sql), Catalog, Enterprise2022));
    }

    [Fact]
    public void Online_nonclustered_create_count_does_not_shift_with_the_edition()
    {
        // On Standard the online row does not apply at all (LOCK-003 territory); the
        // offline-equivalent row is an S table lock, so the answer stays "not Sch-M".
        var statement = ParseSingle("CREATE INDEX IX_T ON dbo.T (C) WITH (ONLINE = ON);");

        Assert.False(DdlOperationClassifier.AcquiresSchMLock(
            statement, Catalog, new PlanizerConfig { Edition = SqlEdition.Standard }));
    }

    private static SqlStatementInfo ParseSingle(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2022);
        Assert.Empty(result.Errors);
        return result.Statements.Single();
    }
}
