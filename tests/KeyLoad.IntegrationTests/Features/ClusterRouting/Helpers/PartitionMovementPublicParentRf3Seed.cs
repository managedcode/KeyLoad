using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual persisted linked models and independent composition commands before public parent execution.</summary>
internal sealed class PartitionMovementPublicParentRf3Seed : IAsyncDisposable
{
    private const string MissingState = "The actual public movement seed is incomplete.";
    private const string CompositionQueue = "parent-composition-jobs";
    private const string CompositionGraph = "parent-composition-links";
    private const string CompositionMessage = "parent-original-link";
    private const string EdgePrefix = "parent-edge-";
    private const string ReversePrefix = "parent-derived-";
    private readonly HttpClient sourceHttp;
    private readonly HttpClient targetHttp;
    private McpOfficialClient? official;
    private readonly List<(CommandRequest Command, CommitReceipt Receipt)> originals = [];
    private readonly List<Func<CancellationToken, Task>> blobReplays = [];

    private PartitionMovementPublicParentRf3Seed(TwoRf3MembershipWave wave, string actualCredential)
    {
        sourceHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        Source = new(sourceHttp, actualCredential, IntegrationClientOptions.Execution());
        Target = new(targetHttp, actualCredential, IntegrationClientOptions.Execution());
    }

    internal KeyLoadClient Source { get; }
    internal KeyLoadClient Target { get; }
    internal McpOfficialClient Official => official ?? throw new InvalidOperationException(MissingState);
    internal RelationalSqlRf3Scenario Models { get => field ?? throw new InvalidOperationException(MissingState); private set; } = null!;
    internal PartitionRef Partition => Models.Partition;
    internal PhysicalOwnerDirectoryV1 Directory { get; private set; } = null!;
    internal AtomicPartitionPlacementResolution OriginalPlacement { get; private set; } = null!;
    internal PartitionMoveRequest FirstRequest { get; private set; } = null!;
    internal IReadOnlyList<(CommandRequest Command, CommitReceipt Receipt)> Originals => originals;
    internal PartitionMovementPublicParentRf3ModelCut OriginalModels { get; private set; } = null!;
    internal BlobMetadata Blob { get; private set; } = null!;
    internal static byte[] BlobRange => [0x41, 0x41, 0x42, 0x42];
    internal static string ForwardMessage => CompositionMessage;
    internal static string DerivedMessage => ReversePrefix + EdgePrefix + CompositionMessage;
    internal static string Graph => CompositionGraph;
    internal static string Queue => CompositionQueue;
    internal static string ComposedEdge => EdgePrefix + CompositionMessage;

    internal static async Task<PartitionMovementPublicParentRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        CancellationToken cancellationToken)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, cancellationToken).ConfigureAwait(false);
        var credential = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, cancellationToken).ConfigureAwait(false);
        var seed = new PartitionMovementPublicParentRf3Seed(wave, credential);
        try
        {
            seed.official = await McpOfficialClient.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
                credential, cancellationToken).ConfigureAwait(false);
            seed.Models = await RelationalSqlRf3Scenario.CreateAsync(seed.Source, cancellationToken).ConfigureAwait(false);
            seed.Directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application,
                wave.Profile, cancellationToken).ConfigureAwait(false);
            seed.OriginalPlacement = await McpCallerAssertions.SdkSuccessAsync(await seed.Source
                .ReadAtomicPartitionPlacementAsync(new(1, seed.Partition), cancellationToken).ConfigureAwait(false));
            var destination = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
                != seed.Directory.ControlOwner.PhysicalShardId).Owner;
            seed.FirstRequest = new(Guid.NewGuid(), seed.Partition, destination.PhysicalShardId,
                seed.OriginalPlacement.Revision, PartitionMoveMode.Transfer);
            await seed.CommitAsync(seed.Models.LinkedModelsCommand(), cancellationToken).ConfigureAwait(false);
            await seed.CompositionAsync(cancellationToken).ConfigureAwait(false);
            await seed.BlobAsync(cancellationToken).ConfigureAwait(false);
            seed.OriginalModels = await PartitionMovementPublicParentRf3Cut.CaptureAsync(seed, cancellationToken).ConfigureAwait(false);
            await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
            return seed;
        }
        catch (Exception primary)
        {
            try
            { await seed.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private async Task CompositionAsync(CancellationToken cancellationToken)
    {
        foreach (var (resource, kind) in new[] { (CompositionQueue, ResourceKind.WorkQueue), (CompositionGraph, ResourceKind.Graph) })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await Source.ConfigureResourceAsync(Guid.NewGuid(),
                new(Partition.TenantId, Partition.DatabaseId, new(resource, kind, Partition.TransactionDomainId)),
                cancellationToken).ConfigureAwait(false));
        }
        var link = new QueueGraphLink(Models.First, Models.Second, RelationalSqlRf3Tokens.EdgeLabel);
        var forward = Models.Command(new EnqueueMessage(CompositionQueue, CompositionMessage,
            JsonSerializer.Serialize(link, JsonDefaults.Options)),
            new QueueToGraph(CompositionGraph, CompositionQueue, EdgePrefix));
        await CommitAsync(forward, cancellationToken).ConfigureAwait(false);
        await CommitAsync(Models.Command(new GraphToQueueMutation(CompositionQueue, CompositionGraph,
            Models.First, ReversePrefix)), cancellationToken).ConfigureAwait(false);
    }

    private async Task CommitAsync(CommandRequest command, CancellationToken cancellationToken)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await Source.CommitAsync(command,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(OriginalPlacement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(OriginalPlacement.PlacementEpoch);
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await Official.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false))).Value);
        originals.Add((command, receipt));
    }

    internal async Task VerifyAsync(CancellationToken cancellationToken)
    {
        await PartitionMovementPublicParentRf3Cut.RequireModelsAsync(this, cancellationToken).ConfigureAwait(false);
        foreach (var replay in blobReplays)
        { await replay(cancellationToken).ConfigureAwait(false); }
    }

    private async Task BlobAsync(CancellationToken cancellationToken)
    {
        var blob = new BlobRef(Partition, RelationalSqlRf3Tokens.Blobs, "parent-native-parts");
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, BlobLimits.RawPartBytes + BlobRange.Length, 0);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await Source.BeginBlobUploadAsync(begin, cancellationToken));
        blobReplays.Add(async token =>
        {
            await SqlRf3Protocol.EqualAsync(begun, await McpCallerAssertions.SdkSuccessAsync(await Source.BeginBlobUploadAsync(begin, token)));
            await SqlRf3Protocol.EqualAsync(begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await Official.CallAsync("keyload_blobs_begin_upload", begin, token))).Value);
        });
        var firstBytes = new byte[BlobLimits.RawPartBytes];
        Array.Fill(firstBytes, (byte)0x41);
        var first = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, firstBytes, BlobIntegrity.PartHash(firstBytes));
        var tailBytes = new byte[] { 0x42, 0x42, 0x42, 0x42 };
        var tail = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 1, tailBytes, BlobIntegrity.PartHash(tailBytes));
        foreach (var request in new[] { first, tail })
        {
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await Source.WriteBlobPartAsync(request, cancellationToken));
            blobReplays.Add(async token =>
            {
                await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await Source.WriteBlobPartAsync(request, token)));
                await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                    await Official.CallAsync("keyload_blobs_write_part", request, token))).Value);
            });
        }
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, first.Ordinal, first.Bytes.Length, first.Sha256);
        hash = BlobIntegrity.NextHash(hash, tail.Ordinal, tail.Bytes.Length, tail.Sha256);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await Source.CompleteBlobUploadAsync(complete, cancellationToken));
        Blob = completed.Value;
        blobReplays.Add(async token =>
        {
            await SqlRf3Protocol.EqualAsync(completed, await McpCallerAssertions.SdkSuccessAsync(await Source.CompleteBlobUploadAsync(complete, token)));
            await SqlRf3Protocol.EqualAsync(completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
                await Official.CallAsync("keyload_blobs_complete_upload", complete, token))).Value);
        });
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (official is { } session)
        { await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        try
        { sourceHttp.Dispose(); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        try
        { targetHttp.Dispose(); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
