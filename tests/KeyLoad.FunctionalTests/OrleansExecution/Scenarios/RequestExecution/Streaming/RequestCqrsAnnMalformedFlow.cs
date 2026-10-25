using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsAnnMalformedFlow
{
    internal static async Task RefuseAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        GrainOperationReply original, RequestCqrsAnnFault fault, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, request.CommandId, OrleansSerialization.Serialize(request));
        var producer = new RequestCqrsProducerObservation();
        var serializer = fixture.Services
            .GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var stream = CqrsStreamLifetime.RunWithPurpose(
            execution => RequestCqrsAnnProducer.Run(id, original, fault, producer,
                fixture.RoutingOptions.Value.MaximumStreamFrames, execution), serializer, id, fixture.Clock,
            producer.MarkActivationSettled, fixture.RoutingOptions, null,
            new CqrsStreamPurpose(() => fixture.Codec.DecodeRequest(envelope, id)), token);
        KeyLoadException? failure = null;
        try
        {
            _ = await GrainRequestStreamConsumer.DrainWithPurposeAsync(_ => stream, serializer, id,
                fixture.Clock, fixture.RoutingOptions,
                new CqrsStreamPurpose(() => fixture.Codec.DecodeRequest(envelope, id)), token);
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
