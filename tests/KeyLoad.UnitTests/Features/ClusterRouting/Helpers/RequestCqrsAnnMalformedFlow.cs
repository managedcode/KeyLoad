using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnMalformedFlow
{
    internal static async Task RefuseAsync(RequestCqrsClusterFixture fixture, AnnMaintenanceRequest request,
        GrainOperationReply original, RequestCqrsAnnFault fault, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, request.CommandId, NativeSerialization.Serialize(request));
        var producer = new RequestCqrsProducerObservation();
        var serializer = fixture.Cluster.ServiceProvider
            .GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var stream = NativeCqrsStreamLifetime.RunWithPurpose(
            execution => RequestCqrsAnnProducer.Run(id, original, fault, producer,
                fixture.RoutingOptions.Value.MaximumTotalFrames, execution), serializer, id, fixture.Clock,
            producer.MarkActivationSettled, fixture.RoutingOptions, null,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)), token);
        KeyLoadException? failure = null;
        try
        {
            _ = await GrainRequestStreamConsumer.DrainWithPurposeAsync(_ => stream, serializer, id,
                fixture.Clock, fixture.RoutingOptions,
                new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)), token);
        }
        catch (KeyLoadException error) { failure = error; }
        await producer.ProducerSettled.Task.WaitAsync(token);
        await producer.ActivationSettled.Task.WaitAsync(token);
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(fault == RequestCqrsAnnFault.FrameBudget
            ? ErrorCode.BudgetExceeded : ErrorCode.OwnershipLost);
        await Assert.That(failure.Message).IsEqualTo(fault == RequestCqrsAnnFault.FrameBudget
            ? GrainRoutingProtocol.ReplyBudgetExceeded : GrainRoutingProtocol.InvalidRequest);
        await Assert.That(producer.ProducerSettlementCount).IsEqualTo(RequestCqrsAnnTestData.OneStep);
        await Assert.That(producer.ActivationSettlementCount).IsEqualTo(RequestCqrsAnnTestData.OneStep);
    }
}
