namespace KeyLoad.UnitTests.RelationalStorage;

internal sealed record SqlTypedInsertState(PrincipalRecord Principal, SqlTypedInsertExecution Created,
    SqlTypedInsertExecution Failed, SqlTypedInsertExecution? Repaired = null, bool EpochFenced = false);
