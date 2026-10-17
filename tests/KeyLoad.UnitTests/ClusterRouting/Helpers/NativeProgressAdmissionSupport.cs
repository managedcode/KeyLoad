using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeProgressAdmissionSupport
{
    internal const int Started = 0;
    internal const int First = 1;
    internal const int FirstSequence = 2;
    internal const int SecondSequence = 3;
    internal const int TerminalSequence = 4;
    internal const int Step = 1;
    internal const long ExpectedRevision = 1;
    internal const long InitialRevision = 10;
    internal const long NextRevision = 11;
    internal const string Root = "root";
    private const string Consumer = "protocol-consumer";
    private const string Collection = "protocol-collection";
    private const string Field = "/text";
    private const byte TerminalMarker = 1;

    internal static NativeCqrsStreamAdmission Admission(RequestCqrsClusterFixture fixture, Guid id,
        OperationKind kind, object request, IOptions<GrainRoutingOptions>? options = null)
    {
        var command = request switch
        {
            OnlineTextIndexMaintenanceRequest online => online.CommandId,
            TextIndexMaintenanceRequest text => text.CommandId,
            ApplyPlacementIntentRequest placement => placement.IntentId,
            _ => throw new InvalidOperationException(nameof(request))
        };
        var signed = fixture.Codec.CreateCommand(id, Root, kind, command, NativeSerialization.Serialize(request));
        return new(Serializer(fixture), id, options ?? fixture.RoutingOptions,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)));
    }

    internal static OnlineTextIndexMaintenanceRequest Online(RequestCqrsClusterFixture fixture)
    {
        var placement = fixture.Database.Database.ReadAtomicPartitionPlacement(Root, new(Step, fixture.Database.Partition));
        return new(Guid.NewGuid(), new(fixture.Database.Partition, Consumer), Collection, Field, Step,
            fixture.Database.Store.Identity.NodeId, new(placement.PhysicalShardId, placement.Incarnation,
                placement.VoterIds, placement.PlacementEpoch));
    }

    internal static ApplyPlacementIntentRequest Placement(RequestCqrsClusterFixture fixture, Guid intent)
        => new(fixture.Database.Partition.TenantId, fixture.Database.Partition.DatabaseId, intent,
            ExpectedRevision, fixture.Database.Partition);

    internal static PlacementIntentProgressV1 Observation(Guid intent, Guid move, int completed, int total, long revision)
        => new(intent, revision, completed, total, move, PartitionMovePhase.Retired);

    internal static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> Terminal(long sequence)
        => RequestCqrsProtocolCases.CompletedChunk(new byte[] { TerminalMarker }, sequence);

    internal static Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Serializer(RequestCqrsClusterFixture fixture)
        => fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();

    internal static long Measure(RequestCqrsClusterFixture fixture, CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> frame)
        => GrainNativeByteCounter.Measure(Serializer(fixture), frame, fixture.RoutingOptions.Value.MaximumCompletedBytes,
            fixture.RoutingOptions, CancellationToken.None);

    internal static async Task RefusedAsync(Action operation, ErrorCode code)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(code);
    }

    internal static async Task CompleteAsync(NativeCqrsStreamAdmission admission,
        IEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames)
    {
        foreach (var frame in frames)
        {
            admission.Admit(frame, CancellationToken.None);
        }
        await Assert.That(admission.CompletionFailure(CancellationToken.None)).IsNull();
    }

    internal static async Task MixedTextAsync(RequestCqrsClusterFixture fixture)
    {
        var id = Guid.NewGuid();
        var online = Online(fixture);
        var request = new TextIndexMaintenanceRequest(online.CommandId, online.Consumer, online.Collection,
            online.Field, online.ConsumerGeneration, online.NodeId, online.Placement, TextIndexMaintenanceMode.Build);
        var admission = Admission(fixture, id, OperationKind.MaintainTextIndex, request);
        var observation = Observation(Guid.NewGuid(), Guid.NewGuid(), Step, Step, InitialRevision);
        var mixed = new GrainRequestProgress(id) { TextPhase = TextIndexMaintenancePhase.Configure, PlacementIntent = observation };
        var invalid = new CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>(CqrsStreamChunkKind.Progress,
            Result<GrainRequestProgress>.Succeed(mixed), null, TextMaintenanceProgress.PhaseCompleted, null, null, FirstSequence);
        admission.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        await RefusedAsync(() => admission.Admit(invalid, CancellationToken.None), ErrorCode.OwnershipLost);
        var healthy = new CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>(CqrsStreamChunkKind.Progress,
            Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(id)
            {
                TextPhase = TextIndexMaintenancePhase.Configure
            }),
            null, TextMaintenanceProgress.PhaseCompleted, null, null, FirstSequence);
        admission.Admit(healthy, CancellationToken.None);
        var started = Admission(fixture, id, OperationKind.MaintainOnlineTextIndex, online);
        var invalidStarted = new CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>(CqrsStreamChunkKind.Started,
            Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(id)
            {
                PlacementIntent = observation
            }), null, null, null, null, Step);
        await RefusedAsync(() => started.Admit(invalidStarted, CancellationToken.None), ErrorCode.OwnershipLost);
        started.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
    }
}
