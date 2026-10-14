namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctRf3Policy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var original = state.Seed.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.DenialAdvance,
            Grants = [new(PostgresRf3State.Partition.DatabaseId, SqlNullOrderingRf3Protocol.Collection, Capability.DocumentsRead)]
        };
        await SqlRf3Protocol.EqualAsync(denied, await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state,
            SqlNullDistinctRf3Cases.All[SqlNullDistinctRf3State.AgainstOne].Clause, ErrorCode.PermissionDenied, token, ast: true);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state,
            SqlNullDistinctRf3Cases.NullClause, ErrorCode.PermissionDenied, token);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state,
            SqlNullDistinctRf3Cases.NotNullClause, ErrorCode.PermissionDenied, token);
        var positive = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.PositiveClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, positive.Clause, ErrorCode.PermissionDenied,
            token, ast: true, astCase: positive);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        var leading = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.LeadingClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, leading.Clause, ErrorCode.PermissionDenied,
            token, ast: true, astCase: leading);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        var escaped = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.EscapeClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, escaped.Clause, ErrorCode.PermissionDenied,
            token, ast: true, astCase: escaped);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        var continuation = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.ContinuationClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, continuation.Clause, ErrorCode.PermissionDenied,
            token, ast: true, astCase: continuation);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        var dollar = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.DollarClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, dollar.Clause, ErrorCode.PermissionDenied,
            token, ast: true, astCase: dollar);
        await SqlRadixIntegerRf3Controls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await SqlUnicodeStringRf3Controls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await SqlUnicodeIdentifierRf3Controls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await SqlNumericSeparatorRf3Controls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        var hex = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.HexClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, hex.Clause, ErrorCode.PermissionDenied,
            token, ast: true, astCase: hex);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        var repaired = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.RepairAdvance
        };
        await SqlRf3Protocol.EqualAsync(repaired, await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await SqlNullDistinctRf3Pages.HealthyAsync(fixture, state, token);
    }
}
