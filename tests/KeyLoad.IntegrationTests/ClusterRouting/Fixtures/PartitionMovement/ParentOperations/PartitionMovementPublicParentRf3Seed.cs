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
    private readonly HttpClient sourceHttp;
    private readonly HttpClient targetHttp;
    private McpOfficialClient? official;
    private readonly List<(CommandRequest Command, CommitReceipt Receipt)> originals = [];
    private readonly List<Func<CancellationToken, Task>> blobReplays = [];

    private PartitionMovementPublicParentRf3Seed(TwoRf3MembershipWave wave, string actualCredential)
    {
        Credential = actualCredential;
        sourceHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        Source = new(sourceHttp, actualCredential, IntegrationClientOptions.Execution());
        Target = new(targetHttp, actualCredential, IntegrationClientOptions.Execution());
    }

    // Borrowed by restored callers only; never diagnostics, a DTO, or trusted role input.
    internal string Credential
    {
        get;
    }
    internal KeyLoadClient Source
    {
        get;
    }
    internal KeyLoadClient Target
    {
        get;
    }
    internal McpOfficialClient Official => official ?? throw new InvalidOperationException(MissingState);
    internal RelationalSqlRf3Scenario Models { get => field ?? throw new InvalidOperationException(MissingState); private set; } = null!;
    internal PartitionRef Partition => Models.Partition;
    internal PhysicalOwnerDirectoryV1 Directory { get; private set; } = null!;
    internal AtomicPartitionPlacementResolution OriginalPlacement { get; private set; } = null!;
    internal PartitionMoveRequest FirstRequest { get; private set; } = null!;
    internal IReadOnlyList<(CommandRequest Command, CommitReceipt Receipt)> Originals => originals;
    internal PartitionMovementPublicParentRf3ModelCut OriginalModels { get; private set; } = null!;
    internal BlobMetadata Blob { get; private set; } = null!;

    internal PartitionMovementPublicParentRf3BlobOriginals BlobOriginals { get; private set; } = null!;
    internal static byte[] BlobRange => [0x41, 0x41, 0x42, 0x42];
    internal static string ForwardMessage => PartitionMovementPublicParentRf3Composition.CompositionMessage;
    internal static string DerivedMessage => PartitionMovementPublicParentRf3Composition.ReversePrefix
        + PartitionMovementPublicParentRf3Composition.EdgePrefix + ForwardMessage;
    internal static string Graph => PartitionMovementPublicParentRf3Composition.CompositionGraph;
    internal static string Queue => PartitionMovementPublicParentRf3Composition.CompositionQueue;
    internal static string ComposedEdge => PartitionMovementPublicParentRf3Composition.EdgePrefix + ForwardMessage;

    internal static async Task<PartitionMovementPublicParentRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        CancellationToken cancellationToken)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, cancellationToken).ConfigureAwait(false);
        var credential = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, cancellationToken).ConfigureAwait(false);
        return await CreateAsync(wave, credential, cancellationToken).ConfigureAwait(false);
    }

    internal static Task<PartitionMovementPublicParentRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        string credential, CancellationToken cancellationToken)
        => CreateCoreAsync(wave, credential, null, cancellationToken);

    internal static Task<PartitionMovementPublicParentRf3Seed> CreateSiblingAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed original, CancellationToken cancellationToken)
        => CreateCoreAsync(wave, original.Credential, original.Models with
        {
            Partition = original.Partition with
            {
                PartitionKey = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat)
            }
        },
            cancellationToken);

    private static async Task<PartitionMovementPublicParentRf3Seed> CreateCoreAsync(TwoRf3MembershipWave wave,
        string credential, RelationalSqlRf3Scenario? existingModels, CancellationToken cancellationToken)
    {
        var seed = new PartitionMovementPublicParentRf3Seed(wave, credential);
        try
        {
            seed.official = await McpOfficialClient.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
                credential, cancellationToken).ConfigureAwait(false);
            seed.Models = existingModels ?? await (wave.SqlInsertTls is { } sql
                ? RelationalSqlRf3Scenario.CreateAsync(seed.Source, sql.Partition, cancellationToken)
                : RelationalSqlRf3Scenario.CreateAsync(seed.Source, cancellationToken)).ConfigureAwait(false);
            seed.Directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application,
                wave.Profile, cancellationToken).ConfigureAwait(false);
            seed.OriginalPlacement = await McpCallerAssertions.SdkSuccessAsync(await seed.Source
                .ReadAtomicPartitionPlacementAsync(new(1, seed.Partition), cancellationToken).ConfigureAwait(false));
            var destination = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
                != seed.Directory.ControlOwner.PhysicalShardId).Owner;
            seed.FirstRequest = new(Guid.NewGuid(), seed.Partition, destination.PhysicalShardId,
                seed.OriginalPlacement.Revision, PartitionMoveMode.Transfer);
            await seed.CommitAsync(seed.Models.LinkedModelsCommand(), cancellationToken).ConfigureAwait(false);
            await seed.CompositionAsync(existingModels is null, cancellationToken).ConfigureAwait(false);
            await seed.BlobAsync(cancellationToken).ConfigureAwait(false);
            seed.OriginalModels = await PartitionMovementPublicParentRf3Cut.CaptureAsync(seed, cancellationToken).ConfigureAwait(false);
            await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
            return seed;
        }
        catch (Exception primary)
        {
            try
            {
                await seed.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }

    private Task CompositionAsync(bool configureResources, CancellationToken cancellationToken)
        => PartitionMovementPublicParentRf3Composition.RunAsync(this, configureResources, cancellationToken);

    internal async Task CommitAsync(CommandRequest command, CancellationToken cancellationToken)
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

    internal async Task ReconnectAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
    {
        if (official is { } original)
        {
            official = null;
            await original.DisposeAsync().ConfigureAwait(false);
        }
        official = await McpOfficialClient.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            Credential, cancellationToken).ConfigureAwait(false);
    }

    internal async Task VerifyAsync(CancellationToken cancellationToken)
    {
        await PartitionMovementPublicParentRf3Cut.RequireModelsAsync(this, cancellationToken).ConfigureAwait(false);
        foreach (var replay in blobReplays)
        {
            await replay(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BlobAsync(CancellationToken cancellationToken)
    {
        await PartitionMovementPublicParentRf3BlobSeed.CreateAsync(this, blobReplays,
            metadata => Blob = metadata, originals => BlobOriginals = originals,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (official is { } session)
        {
            await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        try
        {
            sourceHttp.Dispose();
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        try
        {
            targetHttp.Dispose();
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
