namespace Planizer.MsSql;

/// <summary>
/// Operation keys of the DDL behavior table — string constants matching the
/// <c>operation_key</c> column of <c>mssql-ddl-behavior.csv</c> one-to-one.
/// </summary>
public static class DdlOperationKeys
{
    public const string AddColumnNullable = "add_column_nullable";
    public const string AddColumnNotNullDefaultConst = "add_column_notnull_default_const";
    public const string AddColumnNotNullDefaultNondet = "add_column_notnull_default_nondet";
    public const string AddColumnNotNullNoDefault = "add_column_notnull_no_default";
    public const string AlterColumnWidenVarLen = "alter_column_widen_varlen";
    public const string AlterColumnWidenToMax = "alter_column_widen_to_max";
    public const string AlterColumnFixedLenChange = "alter_column_fixed_len_change";
    public const string AlterColumnNarrow = "alter_column_narrow";
    public const string AlterColumnNullToNotNull = "alter_column_null_to_notnull";
    public const string AlterColumnNotNullToNull = "alter_column_notnull_to_null";
    public const string AlterColumnCollation = "alter_column_collation";
    public const string DropColumn = "drop_column";
    public const string AddComputedPersisted = "add_computed_persisted";
    public const string DataCompressionChange = "data_compression_change";
    public const string CreateClusteredIndexOnHeap = "create_clustered_index_on_heap";
    public const string DropClusteredIndex = "drop_clustered_index";
    public const string CreateNonclusteredIndexOffline = "create_nonclustered_index_offline";
    public const string CreateClusteredIndexOnline = "create_clustered_index_online";
    public const string CreateNonclusteredIndexOnline = "create_nonclustered_index_online";
    public const string AlterIndexRebuildOffline = "alter_index_rebuild_offline";
    public const string AlterIndexRebuildOnline = "alter_index_rebuild_online";
    public const string AlterIndexReorganize = "alter_index_reorganize";
    public const string AddCheckOrFk = "add_check_or_fk";
    public const string AddPkOrUnique = "add_pk_or_unique";
    public const string DropTable = "drop_table";
    public const string TruncateTable = "truncate_table";
    public const string SpRename = "sp_rename";
    public const string AlterTableSwitch = "alter_table_switch";
    public const string AddDefaultConstraint = "add_default_constraint";
    public const string EnableDisableTrigger = "enable_disable_trigger";

    /// <summary>Every key, in CSV order; used by sanity tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        AddColumnNullable,
        AddColumnNotNullDefaultConst,
        AddColumnNotNullDefaultNondet,
        AddColumnNotNullNoDefault,
        AlterColumnWidenVarLen,
        AlterColumnWidenToMax,
        AlterColumnFixedLenChange,
        AlterColumnNarrow,
        AlterColumnNullToNotNull,
        AlterColumnNotNullToNull,
        AlterColumnCollation,
        DropColumn,
        AddComputedPersisted,
        DataCompressionChange,
        CreateClusteredIndexOnHeap,
        DropClusteredIndex,
        CreateNonclusteredIndexOffline,
        CreateClusteredIndexOnline,
        CreateNonclusteredIndexOnline,
        AlterIndexRebuildOffline,
        AlterIndexRebuildOnline,
        AlterIndexReorganize,
        AddCheckOrFk,
        AddPkOrUnique,
        DropTable,
        TruncateTable,
        SpRename,
        AlterTableSwitch,
        AddDefaultConstraint,
        EnableDisableTrigger,
    ];
}
