using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Real old-scope refusals followed by complete new native receipts and literal minimum reads.</summary>
internal static class ClusterRestoreRf3AuthorityTrial
{
    private const string FreshId = "restore-fresh";
    private const string SecondId = "restore-second";
    private const string FreshJson = "{\"restored\":true}";
    private const string PutKind = "putDocument";
    private const long FirstRevision = 1;

    internal static async Task RequireOldFencesAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        foreach (var original in seed.Originals)
        {
            await ErrorAsync(await sdk.CommitAsync(original.Command, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
            await McpCallerAssertions.ErrorAsync(await official.CallAsync(McpCallerTools.DocumentsCommit,
                original.Command, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
            var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit,
                original.Command, original.Command.CommandId);
            await ErrorAsync(await sdk.ExecuteSqlAsync(sql, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
            await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName, sql,
                cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
            var request = new GetDocumentRequest(seed.Models.First, original.Receipt.Token);
            await ErrorAsync(await sdk.GetAsync(request.Reference, original.Receipt.Token,
                cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
            await McpCallerAssertions.ErrorAsync(await official.CallAsync(McpCallerTools.DocumentsGet, request,
                cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
            var minimum = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsGet, request);
            await ErrorAsync(await sdk.ExecuteSqlAsync(minimum, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
            await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName, minimum,
                cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
        }
    }

    internal static async Task<(CommandRequest Command, CommitReceipt Receipt)> RequireFreshAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionRef partition, PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        var first = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(RelationalSqlRf3Tokens.Documents, FreshId, FreshJson)]);
        var second = first with
        {
            CommandId = Guid.NewGuid(),
            Mutations =
            [new PutDocument(RelationalSqlRf3Tokens.Documents, SecondId, FreshJson)]
        };
        var original = await IssueAsync(sdk, first, FreshId, owner, cancellationToken).ConfigureAwait(false);
        await MinimumAsync(sdk, official, partition, FreshId, original.Token, cancellationToken).ConfigureAwait(false);
        var newer = await IssueAsync(sdk, second, SecondId, owner, cancellationToken).ConfigureAwait(false);
        await Assert.That(newer.Token.Position).IsGreaterThan(original.Token.Position);
        await SqlRf3Protocol.EqualAsync(original, await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(first,
            cancellationToken).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(original, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official
            .CallAsync(McpCallerTools.DocumentsCommit, first, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(partition, McpCallerTools.DocumentsCommit, first, first.CommandId);
        await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, cancellationToken));
        await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.McpAsync<CommitReceipt>(official, sql, cancellationToken));
        await MinimumAsync(sdk, official, partition, FreshId, original.Token, cancellationToken).ConfigureAwait(false);
        await MinimumAsync(sdk, official, partition, SecondId, newer.Token, cancellationToken).ConfigureAwait(false);
        return (first, original);
    }

    internal static async Task RequireColdFreshAsync(KeyLoadClient sdk, McpOfficialClient official,
        (CommandRequest Command, CommitReceipt Receipt) original, CancellationToken cancellationToken)
    {
        await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            original.Command, cancellationToken).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official
            .CallAsync(McpCallerTools.DocumentsCommit, original.Command, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(original.Command.Partition, McpCallerTools.DocumentsCommit,
            original.Command, original.Command.CommandId);
        await SqlRf3Protocol.EqualAsync(original.Receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, cancellationToken));
        await SqlRf3Protocol.EqualAsync(original.Receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(official, sql, cancellationToken));
        await MinimumAsync(sdk, official, original.Command.Partition, FreshId, original.Receipt.Token,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CommitReceipt> IssueAsync(KeyLoadClient sdk, CommandRequest command, string id,
        PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, cancellationToken).ConfigureAwait(false));
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(command.CommandId, new(owner.Incarnation,
            command.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch),
            [new MutationReceipt(PutKind, RelationalSqlRf3Tokens.Documents, id, FirstRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        return actual;
    }

    internal static async Task MinimumAsync(KeyLoadClient sdk, McpOfficialClient official, PartitionRef partition,
        string id, CommitToken actualToken, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(partition, RelationalSqlRf3Tokens.Documents, id);
        var expected = new DocumentResult(reference, FirstRevision, FreshJson, false, []);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference,
            actualToken, cancellationToken).ConfigureAwait(false)));
        var request = new GetDocumentRequest(reference, actualToken);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentResult?>(await official
            .CallAsync(McpCallerTools.DocumentsGet, request, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(partition, McpCallerTools.DocumentsGet, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<DocumentResult?>(sdk, sql, cancellationToken));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<DocumentResult?>(official, sql, cancellationToken));
    }

    internal static async Task ErrorAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
}
