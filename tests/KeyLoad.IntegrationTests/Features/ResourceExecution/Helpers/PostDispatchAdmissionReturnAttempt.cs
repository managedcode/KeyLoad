using System.Net.Http.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class PostDispatchAdmissionReturnAttempt
{
    internal static async Task<PostDispatchAdmissionReturnState> RunAsync(ConnectionRf3Scenario scenario,
        bool disconnect, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token, multiplexed: true);
        var command = RequestCqrsPhaseFaultProvisioning.UpdateCommand(scenario.Identity, Guid.NewGuid());
        using var nativeContent = JsonContent.Create(command, command.GetType(), options: JsonDefaults.Options);
        await Assert.That(nativeContent.Headers.ContentLength).IsNull();
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await scenario.Administrator.OutboxStatusAsync(scenario.Identity.Partition, token));
        var baseline = await PostDispatchAdmissionReturnCounters.BaselineAsync(scenario.Administrator, token);
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var held = await scenario.StartHeldAsync(scenario.Identity, command.CommandId, null,
            () => caller.Sdk.CommitAsync(command, selected.Token), token);
        await PostDispatchAdmissionReturnCounters.HeldAsync(scenario.Administrator, baseline, token);
        await Assert.That(held.Original.IsCompleted).IsFalse();
        await OriginalAsync(scenario, outbox, token);
        if (disconnect)
        {
            var failures = new List<Exception>();
            caller.AbortTransport(failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        else
        {
            await selected.CancelAsync();
        }
        await RequestCqrsFaultOutcome.AssertSdkUnknownWriteAsync(await held.Original.WaitAsync(token));
        await scenario.SettleAsync(held.Arm, held.Marker, token);
        await PostDispatchAdmissionReturnCounters.ExactAsync(scenario.Administrator, baseline, token);
        await OriginalAsync(scenario, outbox, token);
        if (disconnect)
        {
            var closed = await scenario.CloseAsync(caller, held.Marker, token);
            await Assert.That(closed.ConnectionId).IsEqualTo(held.Witness.ConnectionId);
            await Assert.That(closed.ActivationId).IsEqualTo(held.Witness.ActivationId);
            await Assert.That(closed.SelectedActivationCount).IsEqualTo(ConnectionRf3Protocol.Absent);
        }
        return new(disconnect, caller, command, held.Witness, outbox);
    }

    private static async Task OriginalAsync(ConnectionRf3Scenario scenario, OutboxStatus outbox, CancellationToken token)
    {
        await scenario.VerifyDocumentAsync(scenario.Identity, RequestCqrsRf3Protocol.DocumentJson,
            ConnectionRf3Protocol.InitialRevision, token);
        var expected = new DocumentReadResult(new(ConnectionRf3PublicOperations.Reference(scenario.Identity),
            ConnectionRf3Protocol.InitialRevision, RequestCqrsRf3Protocol.DocumentJson, false, []));
        await PostDispatchAdmissionReturnContinuation.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await scenario.Administrator.GetAsync(expected.Document!.Reference, token)));
        await PostDispatchAdmissionReturnContinuation.EqualAsync(outbox, await McpCallerAssertions.SdkSuccessAsync(
            await scenario.Administrator.OutboxStatusAsync(scenario.Identity.Partition, token)));
    }
}

internal sealed record PostDispatchAdmissionReturnState(bool Disconnected, ConnectionRf3Caller OriginalCaller,
    CommandRequest Command, ConnectionProbeWitness OriginalOwner, OutboxStatus Before);
