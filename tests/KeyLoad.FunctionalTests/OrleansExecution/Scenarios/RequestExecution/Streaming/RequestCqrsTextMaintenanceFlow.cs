using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.Search;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsTextMaintenanceFlow
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture, TextIndexMaintenanceMode mode,
        RequestCqrsTextFault? fault)
    {
        using var deadline = new CancellationTokenSource(fixture.Timing.CompletionTimeout, TimeProvider.System);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadline.Token);
        var token = operation.Token;
        var lifetime = await RequestCqrsTextResource.ConfigureAsync(fixture.Database, token);
        var request = (await RequestCqrsHostedMaintenanceData.RequestAsync(fixture.Database, token));
        var payload = false;
        var cut = fixture.Database.Store.Position;
        var bytes = KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        if (fault is not null)
        {
            var rejected = await ConsumeAsync(fixture, request, payload, fault, token);
            await Assert.That(rejected.Error).IsTypeOf<KeyLoadException>();
            await Assert.That(((KeyLoadException)rejected.Error!).Code).IsEqualTo(
                fault == RequestCqrsTextFault.FrameBudget ? ErrorCode.BudgetExceeded : ErrorCode.OwnershipLost);
            await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
            await Assert.That(KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store)
                .AsSpan().SequenceEqual(bytes)).IsTrue();
        }
        await RequestCqrsActualTextMaintenanceFlow.RunAsync(fixture, mode, request, lifetime, token);
    }

    private static async Task<Observed> ConsumeAsync(RequestCqrsHostedRuntimeFixture fixture,
        TextIndexMaintenanceRequest request, bool payload, RequestCqrsTextFault? fault, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(id, TextMaintenanceTestValues.Principal,
            OperationKind.MaintainTextIndex, request.CommandId, request);
        var purpose = new CqrsStreamPurpose(() => fixture.Codec.DecodeRequest(envelope, id));
        var producer = new RequestCqrsProducerObservation();
        var kinds = new List<CqrsStreamChunkKind>();
        var phases = new List<TextIndexMaintenancePhase>();
        GrainValue? final = null;
        Exception? failure = null;
        var stream = CqrsStreamLifetime.RunWithPurpose(
            execution => RequestCqrsTextProducer.Run(id, payload, fault, producer, fixture.RoutingOptions.Value.MaximumStreamFrames, execution),
            fixture.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, producer.MarkActivationSettled, fixture.RoutingOptions, null, purpose, token);
        try
        {
            await foreach (var chunk in stream.WithCancellation(token))
            {
                kinds.Add(chunk.Kind);
                if (chunk.ProgressResult?.Value?.TextPhase is { } phase)
                {
                    phases.Add(phase);
                }
                if (chunk.Final?.Value is { } result)
                {
                    final = GrainReplyFactory.RequireValue(result);
                }
            }
        }
        catch (KeyLoadException error) { failure = error; }
        await Assert.That(producer.Started.Task.IsCompletedSuccessfully).IsTrue()
            .Because(failure?.Message ?? "The controlled CQRS producer must start before validation.");
        await producer.ProducerSettled.Task.WaitAsync(token);
        await producer.ActivationSettled.Task.WaitAsync(token);
        await Assert.That(producer.ProducerSettlementCount).IsEqualTo(1);
        await Assert.That(producer.ActivationSettlementCount).IsEqualTo(1);
        return new(kinds.ToArray(), phases.ToArray(), final, failure);
    }

    private sealed record Observed(CqrsStreamChunkKind[] Kinds, TextIndexMaintenancePhase[] Phases,
        GrainValue? Result, Exception? Error);
}
