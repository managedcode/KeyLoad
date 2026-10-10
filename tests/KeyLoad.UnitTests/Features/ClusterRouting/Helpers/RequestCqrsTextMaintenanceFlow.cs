using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.Search;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsTextMaintenanceFlow
{
    internal static async Task RunAsync(RequestCqrsClusterFixture fixture, TextIndexMaintenanceMode mode,
        RequestCqrsTextFault? fault)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        fixture.Database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
        var request = (await NativeTextMaintenanceRequestFixture.CreateAsync(fixture.Database, token));
        var payload = NativeSerialization.Serialize(false);
        var cut = fixture.Database.Store.Position;
        var bytes = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        if (fault is not null)
        {
            var rejected = await ConsumeAsync(fixture, request, payload, fault, token);
            await Assert.That(rejected.Error).IsTypeOf<KeyLoadException>();
            await Assert.That(((KeyLoadException)rejected.Error!).Code).IsEqualTo(
                fault == RequestCqrsTextFault.FrameBudget ? ErrorCode.BudgetExceeded : ErrorCode.OwnershipLost);
            await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
            await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store)
                .AsSpan().SequenceEqual(bytes)).IsTrue();
        }
        await RequestCqrsActualTextMaintenanceFlow.RunAsync(fixture, mode, request, token);
    }

    private static async Task<Observed> ConsumeAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, byte[] payload, RequestCqrsTextFault? fault, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(id, NativeTextMaintenanceTestValues.Principal,
            OperationKind.MaintainTextIndex, request.CommandId, NativeSerialization.Serialize(request));
        var purpose = new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id));
        var producer = new RequestCqrsProducerObservation();
        var kinds = new List<CqrsStreamChunkKind>();
        var phases = new List<TextIndexMaintenancePhase>();
        ReadOnlyMemory<byte> final = default;
        Exception? failure = null;
        var stream = NativeCqrsStreamLifetime.RunWithPurpose(
            execution => RequestCqrsTextProducer.Run(id, payload, fault, producer, fixture.RoutingOptions.Value.MaximumTotalFrames, execution),
            fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, producer.MarkActivationSettled, fixture.RoutingOptions, null, purpose, token);
        try
        {
            await foreach (var chunk in stream.WithCancellation(token))
            {
                kinds.Add(chunk.Kind);
                if (chunk.ProgressResult?.Value?.TextPhase is { } phase)
                { phases.Add(phase); }
                if (chunk.Final?.Value is { } result)
                { final = result.Payload; }
            }
        }
        catch (KeyLoadException error) { failure = error; }
        await producer.ProducerSettled.Task;
        await producer.ActivationSettled.Task;
        await Assert.That(producer.ProducerSettlementCount).IsEqualTo(1);
        await Assert.That(producer.ActivationSettlementCount).IsEqualTo(1);
        return new(kinds.ToArray(), phases.ToArray(), final, failure);
    }

    private sealed record Observed(CqrsStreamChunkKind[] Kinds, TextIndexMaintenancePhase[] Phases,
        ReadOnlyMemory<byte> Payload, Exception? Error);
}
