using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Real complete caller flow; no result from its source replaces native Linux qualification.</summary>
internal static class ClusterRestoreRf3Scenario
{
    private const string OffNodeDirectory = "off-node";
    private const string DurationLabel = "Actual whole RF3 capture/off-node/CLI/restore/cold duration: ";
    private const string RtoLabel = "Actual whole CLI publication/target startup/public verification/cold continuation RTO: ";
    private const string DurationFormat = "c";
    private const int FirstOwnerIndex = 0;
    private const string PostCaptureId = "after-backup";
    private const string PostCaptureJson = "{\"afterBackup\":true}";

    internal static Task RunAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        string ownedRoot, CancellationToken cancellationToken)
        => RunAsync(source, seed, ownedRoot, null, cancellationToken);

    internal static async Task RunAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        string ownedRoot, IReadOnlyList<string>? invalidCredentials, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var secondary = await ClusterRestoreRf3SecondaryPartition.SeedAsync(source, seed, cancellationToken).ConfigureAwait(false);
        var eventing = await ClusterRestoreRf3EventingSeed.CreateAsync(source, seed, cancellationToken).ConfigureAwait(false);
        var originals = await ClusterRestoreRf3Capture.CaptureAsync(source, seed.Partition, Guid.NewGuid(),
            cancellationToken).ConfigureAwait(false);
        var postCapture = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents,
            PostCaptureId, PostCaptureJson));
        var postCaptureReceipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(postCapture,
            cancellationToken).ConfigureAwait(false));
        await ClusterRestoreRf3RpoOracle.RequireAsync(seed, originals, postCapture, postCaptureReceipt,
            PostCaptureId, cancellationToken).ConfigureAwait(false);
        var archives = await ClusterRestoreRf3NativeArchive.CopyAsync(source, originals,
            Path.Combine(ownedRoot, OffNodeDirectory), cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3ColdCapture.RequireAsync(source, seed, originals, archives,
            cancellationToken).ConfigureAwait(false);
        var target = new ClusterRestoreRf3Fixture(ownedRoot, source.Profile.AdminKey, originals, archives);
        Exception? initiatingFailure = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                await target.StartRejectedManifestAsync(cancellationToken).ConfigureAwait(false);
                await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, cancellationToken).ConfigureAwait(false);
                await ClusterRestoreRf3ArchiveOmissionTrial.RequireAsync(target, originals, archives, ownedRoot,
                    cancellationToken).ConfigureAwait(false);
                await RestoreAndVerifyAsync(target, seed, originals, eventing, secondary,
                    invalidCredentials, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception original)
            {
                initiatingFailure = original;
                throw;
            }
            finally { await target.DisposeAsync().ConfigureAwait(false); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (initiatingFailure is not null && !cleanupCompleted)
            { throw new AggregateException(initiatingFailure, terminal); }
            throw;
        }
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        await Assert.That(elapsed).IsLessThan(ClusterRestoreRf3Protocol.ParentDeadline);
        await TestContext.Current!.OutputWriter.WriteLineAsync(DurationLabel
            + elapsed.ToString(DurationFormat, CultureInfo.InvariantCulture));
    }

    private static async Task RestoreAndVerifyAsync(ClusterRestoreRf3Fixture target,
        PartitionMovementPublicParentRf3Seed seed, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ClusterRestoreRf3EventingState eventing, ClusterRestoreRf3SecondaryState secondary, IReadOnlyList<string>? invalidCredentials, CancellationToken cancellationToken)
    {
        var restoreStarted = TimeProvider.System.GetTimestamp();
        await target.StartAsync(restore: true, cancellationToken).ConfigureAwait(false);
        var nativeNodes = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals,
            dispatchPaused: true).ConfigureAwait(false);
        await target.RequireOperatorReceiptAsync(nativeNodes).ConfigureAwait(false);
        await Assert.That(target.OriginalOperatorExits).IsEquivalentTo(new[] { ClusterRestoreRf3Protocol.FailedExit, ClusterRestoreRf3Protocol.FailedExit, ClusterRestoreRf3Protocol.SuccessfulExit },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await target.StartAsync(restore: false, cancellationToken).ConfigureAwait(false);
        if (invalidCredentials is not null)
        {
            await ClusterRestoreRf3CredentialTrial.RequireTargetAsync(target, seed.Partition, invalidCredentials,
            cancellationToken).ConfigureAwait(false);
        }
        await ClusterRestoreRf3SecondaryPartition.RequireTargetAsync(target, seed.Credential, secondary, originals, cancellationToken).ConfigureAwait(false);
        var fresh = await CallersAsync(target, seed, originals, eventing, null, cancellationToken).ConfigureAwait(false);
        _ = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals, dispatchPaused: false).ConfigureAwait(false);
        await target.StartAsync(restore: false, cancellationToken).ConfigureAwait(false);
        if (invalidCredentials is not null)
        {
            await ClusterRestoreRf3CredentialTrial.RequireTargetAsync(target, seed.Partition, invalidCredentials,
            cancellationToken).ConfigureAwait(false);
        }
        await ClusterRestoreRf3SecondaryPartition.RequireTargetAsync(target, seed.Credential, secondary, originals, cancellationToken).ConfigureAwait(false);
        _ = await CallersAsync(target, seed, originals, eventing, fresh, cancellationToken).ConfigureAwait(false);
        var elapsed = TimeProvider.System.GetElapsedTime(restoreStarted);
        await Assert.That(elapsed).IsLessThan(ClusterRestoreRf3Protocol.ParentDeadline);
        await TestContext.Current!.OutputWriter.WriteLineAsync(RtoLabel + elapsed.ToString(DurationFormat,
            CultureInfo.InvariantCulture));
    }

    internal static async Task<(CommandRequest Command, CommitReceipt Receipt)> CallersAsync(ClusterRestoreRf3Fixture target, PartitionMovementPublicParentRf3Seed seed,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, ClusterRestoreRf3EventingState eventing, (CommandRequest Command, CommitReceipt Receipt)? fresh,
        CancellationToken cancellationToken)
    {
        var effective = originals.SelectMany(receipt => receipt.Cut.Partitions)
            .First(partition => partition.Roster.Partition == seed.Partition).Placement.PhysicalShardId;
        var index = Array.FindIndex(target.Mappings.ToArray(), mapping => mapping.Source.PhysicalShardId == effective);
        if (index < FirstOwnerIndex)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var node = index == FirstOwnerIndex ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4;
        return await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, node,
            seed.Credential, (sdk, official) => RequireCallersAsync(target, seed, originals, eventing, fresh,
                sdk, official, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt)> RequireCallersAsync(
        ClusterRestoreRf3Fixture target, PartitionMovementPublicParentRf3Seed seed,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, ClusterRestoreRf3EventingState eventing, (CommandRequest Command, CommitReceipt Receipt)? fresh,
        KeyLoadClient sdk, McpOfficialClient official, CancellationToken cancellationToken)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(
            new(ClusterRestoreRf3Protocol.CurrentVersion, seed.Partition), cancellationToken).ConfigureAwait(false));
        var originalPartition = originals.SelectMany(receipt => receipt.Cut.Partitions)
            .First(partition => partition.Roster.Partition == seed.Partition);
        var mapping = target.Mappings.Single(item => item.Source.PhysicalShardId == originalPartition.Placement.PhysicalShardId);
        var old = originalPartition.Placement;
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(old.Version, seed.Partition,
            mapping.Target.PhysicalShardId, mapping.Target.Incarnation, mapping.Target.VoterIds,
            mapping.Target.PlacementEpoch, old.DirectoryRevision, old.Revision, old.IsFallback), placement);
        var original = originals.Single(receipt => receipt.Cut.Owner.PhysicalShardId == mapping.Source.PhysicalShardId);
        await ClusterRestoreRf3AuthorityTrial.RequireOldFencesAsync(sdk, official, seed, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3ModelOracle.RequireAsync(sdk, official, seed,
            original.Cut.StorePosition, cancellationToken).ConfigureAwait(false);
        await PostCaptureAbsentAsync(sdk, official, seed.Partition, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3EventingReadOracle.RequireAsync(sdk, official, eventing, cancellationToken).ConfigureAwait(false);
        if (fresh is null)
        {
            await ClusterRestoreRf3EventingFences.RequireAsync(sdk, official, eventing, cancellationToken).ConfigureAwait(false);
            await PauseThenResumeAsync(target, sdk, official, seed, original.Cut.StorePosition, cancellationToken).ConfigureAwait(false);
            await ClusterRestoreRf3EventingContinuation.RunAsync(sdk, official, eventing, mapping.Target,
                cancellationToken).ConfigureAwait(false);
            var current = await ClusterRestoreRf3AuthorityTrial.RequireFreshAsync(sdk, official, seed.Partition,
                mapping.Target, cancellationToken).ConfigureAwait(false);
            eventing.FinalOutbox = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(seed.Partition,
                cancellationToken).ConfigureAwait(false));
            return current;
        }
        await ClusterRestoreRf3AuthorityTrial.RequireColdFreshAsync(sdk, official, fresh.Value,
            cancellationToken).ConfigureAwait(false);
        return fresh.Value;
    }

    private static async Task PostCaptureAbsentAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(partition, RelationalSqlRf3Tokens.Documents, PostCaptureId);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, cancellationToken))).IsNull();
        var request = new GetDocumentRequest(reference);
        await Assert.That((await McpCallerAssertions.SuccessAsync<DocumentResult?>(await official.CallAsync(
            McpCallerTools.DocumentsGet, request, cancellationToken))).Value).IsNull();
        var sql = SqlRf3Protocol.Call(partition, McpCallerTools.DocumentsGet, request);
        await Assert.That(await SqlRf3Protocol.SdkAsync<DocumentResult?>(sdk, sql, cancellationToken)).IsNull();
        await Assert.That(await SqlRf3Protocol.McpAsync<DocumentResult?>(official, sql, cancellationToken)).IsNull();
    }

    private static async Task PauseThenResumeAsync(ClusterRestoreRf3Fixture target, KeyLoadClient sdk, McpOfficialClient official,
        PartitionMovementPublicParentRf3Seed seed, long originalCanonicalPosition, CancellationToken cancellationToken)
    {
        var receive = new ReceiveRequest(Guid.NewGuid(), new(seed.Partition, RelationalSqlRf3Tokens.Queue));
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ReceiveAsync(receive,
            cancellationToken).ConfigureAwait(false), ErrorCode.DispatchPaused);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(McpCallerTools.MessagesReceive, receive,
            cancellationToken).ConfigureAwait(false), ErrorCode.DispatchPaused, dispatched: true);
        await ResumeAllAsync(target, seed.Credential, seed.Partition, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3ModelOracle.RequireAsync(sdk, official, seed,
            originalCanonicalPosition, cancellationToken).ConfigureAwait(false);
    }
    private static async Task ResumeAllAsync(ClusterRestoreRf3Fixture target, string actualCredential,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            _ = await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, node, actualCredential,
                async (sdk, official) =>
                {
                    var commandId = Guid.NewGuid();
                    await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.SetDispatchAsync(commandId,
                        paused: false, cancellationToken).ConfigureAwait(false))).IsTrue();
                    var arguments = McpOfficialClient.Arguments(false);
                    arguments.Add(McpCallerProtocol.CommandId, commandId);
                    await Assert.That((await McpCallerAssertions.SuccessAsync<bool>(await official.Client.InvokeKeyLoadToolAsync(
                        McpCallerTools.AdminDispatch, arguments, cancellationToken: cancellationToken).ConfigureAwait(false))).Value).IsTrue();
                    var sql = SqlRf3Protocol.Call(partition, McpCallerTools.AdminDispatch, false, commandId);
                    await Assert.That(await SqlRf3Protocol.SdkAsync<bool>(sdk, sql, cancellationToken)).IsTrue();
                    await Assert.That(await SqlRf3Protocol.McpAsync<bool>(official, sql, cancellationToken)).IsTrue();
                    await ClusterRestoreRf3ResumeConflict.RequireAsync(sdk, official, partition, commandId,
                        cancellationToken).ConfigureAwait(false);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
        }
    }
}
