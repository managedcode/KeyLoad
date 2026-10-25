namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNullDistinctClusterPolicy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var original = state.Seed.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.DenialAdvance,
            Grants = [new(PostgresClusterState.Partition.DatabaseId, SqlNullOrderingClusterProtocol.Collection, Capability.DocumentsRead)]
        };
        await SqlClusterProtocol.EqualAsync(denied, await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, SqlNullDistinctClusterCases.All[SqlNullDistinctClusterState.AgainstOne].Command, ErrorCode.PermissionDenied, token, ast: true);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NullClause + SqlNullDistinctClusterCases.Tail), ErrorCode.PermissionDenied, token);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NotNullClause + SqlNullDistinctClusterCases.Tail), ErrorCode.PermissionDenied, token);
        var positive = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.PositiveClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, positive.Command, ErrorCode.PermissionDenied, token, ast: true, astCase: positive);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var leading = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.LeadingClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, leading.Command, ErrorCode.PermissionDenied, token, ast: true, astCase: leading);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var escaped = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.EscapeClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, escaped.Command, ErrorCode.PermissionDenied, token, ast: true, astCase: escaped);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var continuation = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.ContinuationClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, continuation.Command, ErrorCode.PermissionDenied, token, ast: true, astCase: continuation);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var dollar = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.DollarClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, dollar.Command, ErrorCode.PermissionDenied, token, ast: true, astCase: dollar);
        await SqlRadixIntegerClusterControls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await SqlUnicodeStringClusterControls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await SqlUnicodeIdentifierClusterControls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await SqlNumericSeparatorClusterControls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var hex = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.HexClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, hex.Command, ErrorCode.PermissionDenied, token, ast: true, astCase: hex);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var repaired = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.RepairAdvance
        };
        await SqlClusterProtocol.EqualAsync(repaired, await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await SqlNullDistinctClusterPages.HealthyAsync(fixture, state, token);
    }
}
