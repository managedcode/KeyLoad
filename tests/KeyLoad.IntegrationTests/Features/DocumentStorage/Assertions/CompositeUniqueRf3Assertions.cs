using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class CompositeUniqueRf3Assertions
{
    internal static QueryRequest Query(PartitionRef partition, string? predicate = null)
        => new(partition, "SELECT * FROM \"composite-unique\""
            + (predicate is null ? "" : " WHERE " + predicate) + " ORDER BY id ASC LIMIT 10", AllowFullScan: predicate is null);

    internal static async Task RowsAsync(KeyLoadClient sdk, McpOfficialClient mcp, PartitionRef partition,
        string? predicate, long minimumCut, QueryRow[] expected, CancellationToken token)
    {
        var request = Query(partition, predicate);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, request, token))).Value;
        foreach (var page in new[] { direct, official })
        {
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.AccessPath).IsEqualTo(predicate is null ? "bounded-full-scan" : "index:" + CompositeUniqueRf3Scenario.Index);
            await Assert.That(page.Rows.Select(row => row.EntityId)).IsEquivalentTo(expected.Select(row => row.EntityId), CollectionOrdering.Matching);
            for (var i = 0; i < expected.Length; i++)
            {
                await Assert.That(page.Rows[i].Revision).IsEqualTo(expected[i].Revision);
                await Assert.That(page.Rows[i].Json).IsEqualTo(expected[i].Json);
                await Assert.That(page.Rows[i].Redacted).IsFalse();
                await Assert.That(page.Rows[i].RedactedFields.GetValueOrDefault().IsDefaultOrEmpty).IsTrue();
            }
        }
        await Assert.That(JsonDefaults.Serialize(direct.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(official.Rows))).IsTrue();
    }

    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, bool officialFirst, CancellationToken token)
    {
        var direct = officialFirst ? null : await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        var official = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value;
        direct ??= await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(direct.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(direct.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(direct.Mutations.Length).IsEqualTo(command.Mutations.Length);
        await Assert.That(JsonDefaults.Serialize(direct).AsSpan().SequenceEqual(JsonDefaults.Serialize(official))).IsTrue();
        return direct;
    }

    internal static async Task RejectAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(result.Problem.StatusCode).IsEqualTo(Errors.Status(code));
        await Assert.That(result.Problem.Detail).IsEqualTo(Detail(code));
    }

    private static string Detail(ErrorCode code) => code == ErrorCode.Conflict
        ? "A partition-scoped unique index value is already present."
        : "The principal cannot perform this operation in this scope.";

    internal static async Task RejectedCommitAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, ErrorCode code, string secret, CancellationToken token)
    {
        var direct = await sdk.CommitAsync(command, token);
        await RejectAsync(direct, code);
        var official = await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(official, code, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Detail(code));
        await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, CompositeUniqueRf3Scenario.ConflictJson);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, "private-rollback-canary");
        await Assert.That(JsonSerializer.Serialize(direct.Problem, JsonDefaults.Options).Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(JsonSerializer.Serialize(direct.Problem, JsonDefaults.Options).Contains("private-rollback-canary", StringComparison.Ordinal)).IsFalse();
    }
}
