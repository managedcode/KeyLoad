using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlUnicodeStringClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var (command, code) in new (Func<NpgsqlCommand>, ErrorCode)[]
        {
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\123'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\+110000'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\D800'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\DC00'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\D800x\\DC00'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\0000'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\+000000'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'\\g000'" + SqlNullDistinctClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'unterminated" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'low' UESCAPE" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'low' UESCAPE ''" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'low' UESCAPE '!!'" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'low' UESCAPE '+'" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'low' UESCAPE 'a'" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'low' UESCAPE ' '" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlNullDistinctClusterCases.Prefix + "label = U&'low' UESCAPE 'é'" + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation)
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
        foreach (var clause in new[] { SqlNullDistinctClusterCases.UnicodeClause, SqlNullDistinctClusterCases.UnicodeCustomClause })
        {
            var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, item.Command, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

}
