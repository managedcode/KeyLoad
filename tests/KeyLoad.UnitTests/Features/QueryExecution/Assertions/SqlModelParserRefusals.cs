using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlModelParserRefusals
{
    internal static async Task DeniedAsync(SqlModelParserOperationFixture fixture)
    {
        var engine = new QueryEngine(fixture.Owner, UnitExecutionOptions.QueryExecution());
        await RefuseAsync(fixture, () => engine.Execute(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.EventSql, AllowFullScan: true)), ErrorCode.PermissionDenied);
        await RefuseAsync(fixture, () => engine.ExecuteAst(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.Events(), AllowFullScan: true)), ErrorCode.PermissionDenied);
        await RefuseAsync(fixture, () => engine.Execute(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.QueueSql, AllowFullScan: true)), ErrorCode.PermissionDenied);
        await RefuseAsync(fixture, () => engine.ExecuteAst(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.Queue, AllowFullScan: true)), ErrorCode.PermissionDenied);
    }
    internal static async Task StaleAsync(SqlModelParserOperationFixture fixture)
    {
        var engine = new QueryEngine(fixture.Owner, UnitExecutionOptions.QueryExecution());
        await RefuseAsync(fixture, () => engine.Execute(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.StaleSql, AllowFullScan: true)), ErrorCode.TokenInvalidated,
            SqlModelParserOperationData.StaleDetail);
        await RefuseAsync(fixture, () => engine.ExecuteAst(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.Events(SqlModelParserOperationData.StaleGeneration), AllowFullScan: true)),
            ErrorCode.TokenInvalidated, SqlModelParserOperationData.StaleDetail);
    }
    private static async Task RefuseAsync(SqlModelParserOperationFixture fixture, Action action,
        ErrorCode code, string? detail = null)
    {
        var before = SqlModelParserConservation.Capture(fixture);
        var error = Assert.ThrowsExactly<KeyLoadException>(action);
        await Assert.That(error.Code).IsEqualTo(code);
        if (detail is not null)
        { await Assert.That(error.Message).IsEqualTo(detail); }
        await Assert.That(error.InnerException).IsNull();
        await SqlModelParserConservation.SameAsync(fixture, before);
        await SqlModelParserConservation.ReplayAsync(fixture);
    }
}
