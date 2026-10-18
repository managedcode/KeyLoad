using KeyLoad.Clustering;
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
    internal const int Step = 1;
    internal const string Root = "root";
    private const string Consumer = "protocol-consumer";
    private const string Collection = "protocol-collection";
    private const string Field = "/text";
    private const byte TerminalMarker = 1;

    internal static NativeCqrsStreamAdmission Admission(RequestCqrsHostedRuntimeFixture fixture, Guid id,
        OperationKind kind, object request, IOptions<GrainRoutingOptions>? options = null)
    {
        var command = request switch
        {
            OnlineTextIndexMaintenanceRequest online => online.CommandId,
            TextIndexMaintenanceRequest text => text.CommandId,
            _ => throw new InvalidOperationException(nameof(request))
        };
        var signed = fixture.Codec.CreateCommand(id, Root, kind, command, NativeSerialization.Serialize(request));
        return new(Serializer(fixture), id, options ?? fixture.RoutingOptions,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)));
    }

    internal static OnlineTextIndexMaintenanceRequest Online(RequestCqrsHostedRuntimeFixture fixture)
    {
        var placement = fixture.Database.Database.ReadAtomicPartitionPlacement(Root, fixture.Database.Partition);
        return new(Guid.NewGuid(), new(fixture.Database.Partition, Consumer), Collection, Field, Step,
            fixture.Database.Store.Identity.NodeId, new(placement.PhysicalShardId, placement.Incarnation));
    }

    internal static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> Terminal(long sequence)
        => RequestCqrsProtocolCases.CompletedChunk(new byte[] { TerminalMarker }, sequence);

    internal static Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Serializer(RequestCqrsHostedRuntimeFixture fixture)
        => fixture.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();

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
}
