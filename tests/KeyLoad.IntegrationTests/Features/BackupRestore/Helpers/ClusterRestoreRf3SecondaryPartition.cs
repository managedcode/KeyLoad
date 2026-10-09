using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3SecondaryPartition
{
    private const string DocumentId = "restore-secondary";
    private const string DocumentJson = "{\"partition\":\"secondary\"}";
    private const string PutKind = "putDocument";
    private const long FirstRevision = 1;
    private const int FirstOwnerIndex = 0;

    internal static async Task<ClusterRestoreRf3SecondaryState> SeedAsync(TwoRf3MembershipWave source,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var partition = seed.Partition with { PartitionKey = Guid.NewGuid().ToString(ClusterRestoreRf3Protocol.ArchiveFormat) };
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(RelationalSqlRf3Tokens.Documents, DocumentId, DocumentJson)]);
        return await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async (sdk, official) =>
            {
                var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command,
                    cancellationToken).ConfigureAwait(false));
                await Assert.That(actual.Token.Position).IsGreaterThan(0L);
                await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
                    new(source.Profile.Incarnation, partition.AtomicPartitionId, actual.Token.Position,
                        ClusterRestoreRf3Protocol.InitialOwnerEpoch),
                    [new(PutKind, RelationalSqlRf3Tokens.Documents, DocumentId, FirstRevision)],
                    DurabilityProfile.QuorumProcessDurable), actual);
                var document = new DocumentResult(new(partition, RelationalSqlRf3Tokens.Documents, DocumentId),
                    FirstRevision, DocumentJson, false, []);
                await ClusterRestoreRf3EventingReadOracle.FourAsync(actual, await McpCallerAssertions.SdkSuccessAsync(
                    await sdk.CommitAsync(command, cancellationToken).ConfigureAwait(false)), sdk, official, partition,
                    McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false);
                await ReadAsync(sdk, official, document, cancellationToken).ConfigureAwait(false);
                return new ClusterRestoreRf3SecondaryState(command, actual, document);
            }, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task RequireTargetAsync(ClusterRestoreRf3Fixture target, string actualCredential,
        ClusterRestoreRf3SecondaryState state, System.Collections.Immutable.ImmutableArray<ClusterBackupOwnerReceipt> originals,
        CancellationToken cancellationToken)
    {
        var cuts = originals.SelectMany(receipt => receipt.Cut.Partitions)
            .Where(cut => cut.Roster.Partition == state.Command.Partition).ToArray();
        await Assert.That(cuts.Length).IsGreaterThan(FirstOwnerIndex);
        var effective = cuts[FirstOwnerIndex].Placement.PhysicalShardId;
        var index = Array.FindIndex(target.Mappings.ToArray(), mapping => mapping.Source.PhysicalShardId == effective);
        if (index < FirstOwnerIndex)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var node = index == FirstOwnerIndex ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4;
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, node, actualCredential,
            async (sdk, official) =>
            {
                await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.CommitAsync(state.Command,
                    cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
                await McpCallerAssertions.ErrorAsync(await official.CallAsync(McpCallerTools.DocumentsCommit,
                    state.Command, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
                await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.GetAsync(state.Document.Reference,
                    state.Receipt.Token, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
                await McpCallerAssertions.ErrorAsync(await official.CallAsync(McpCallerTools.DocumentsGet,
                    new GetDocumentRequest(state.Document.Reference, state.Receipt.Token), cancellationToken).ConfigureAwait(false),
                    ErrorCode.TokenInvalidated, dispatched: true);
                var oldCommit = SqlRf3Protocol.Call(state.Command.Partition, McpCallerTools.DocumentsCommit,
                    state.Command, state.Command.CommandId);
                await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ExecuteSqlAsync(oldCommit,
                    cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
                await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName,
                    oldCommit, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
                var oldMinimum = SqlRf3Protocol.Call(state.Command.Partition, McpCallerTools.DocumentsGet,
                    new GetDocumentRequest(state.Document.Reference, state.Receipt.Token));
                await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ExecuteSqlAsync(oldMinimum,
                    cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
                await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName,
                    oldMinimum, cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
                await ReadAsync(sdk, official, state.Document, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadAsync(KeyLoadClient sdk, McpOfficialClient official, DocumentResult expected,
        CancellationToken cancellationToken)
        => await ClusterRestoreRf3EventingReadOracle.FourAsync<DocumentResult?>(expected,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(expected.Reference,
                cancellationToken).ConfigureAwait(false)), sdk, official, expected.Reference.Partition,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(expected.Reference), cancellationToken).ConfigureAwait(false);
}
