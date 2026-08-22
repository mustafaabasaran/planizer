namespace Planizer.MsSql;

/// <summary>Coarse classification of a T-SQL statement (RULES.md section 1).</summary>
public enum StatementKind
{
    /// <summary>Create / Alter / Drop / Truncate / Rename family, including <c>sp_rename</c>.</summary>
    Ddl,

    /// <summary>Insert / Update / Delete / Merge / Select.</summary>
    Dml,

    /// <summary>Grant / Deny / Revoke.</summary>
    Dcl,

    /// <summary>Control flow: If / While / Begin-End / Try / transaction / SET statements.</summary>
    Flow,

    /// <summary>Dynamic SQL (<c>EXEC('…')</c>, <c>EXEC @sql</c>, <c>sp_executesql</c>): cannot be analyzed statically.</summary>
    Dynamic,

    /// <summary>Everything else, e.g. a plain stored procedure call.</summary>
    Other,
}
