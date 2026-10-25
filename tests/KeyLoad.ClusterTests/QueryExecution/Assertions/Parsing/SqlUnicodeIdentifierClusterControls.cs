using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlUnicodeIdentifierClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var (command, code) in new (Func<NpgsqlCommand>, ErrorCode)[]
        {
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\\D800\" = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\\DC00\" = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\\0000\" = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\\+110000\" = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\\zzzz\" = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\" UESCAPE '0' = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\" UESCAPE '!!' = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "U&\"score\" UESCAPE '\u00e9' = 1" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation)
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, code, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var (command, code) in new (Func<NpgsqlCommand>, ErrorCode)[]
        {
            (() => new(SqlNullDistinctClusterCases.WindowPrefix + SqlUnicodeIdentifierCases.Clause + OrderBy + "OFFSET \"1\" ROWS FETCH NEXT 1 ROWS ONLY"), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.WindowPrefix + SqlUnicodeIdentifierCases.Clause + OrderBy + "OFFSET 0 ROWS FETCH NEXT \"1\" ROW ONLY"), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.WindowPrefix + SqlUnicodeIdentifierCases.Clause + OrderBy + "OFFSET 0 ROWS FETCH NEXT N'1' ROW ONLY"), ErrorCode.Validation)
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, code, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }

    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root,
        ErrorCode code, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { SqlUnicodeIdentifierCases.Clause,
            SqlUnicodeIdentifierCases.WideClause, SqlUnicodeIdentifierCases.CustomClause })
        {
            var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, item.Command, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static bool IsSelected(SqlNullDistinctClusterCase item)
        => item.Clause is SqlUnicodeIdentifierCases.Clause or SqlUnicodeIdentifierCases.WideClause or SqlUnicodeIdentifierCases.CustomClause;

    private const string OrderBy = " ORDER BY label ASC ";
}
