using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Faults only after genuine settlement and observes authentic sender outcome reconciliation.</summary>
internal static class ProtectedDocumentUncertaintyFlow
{
    internal static async Task<CommitReceipt> RunAsync(ProtectedDocumentUncertaintyScenario owner, TwoRf3MembershipWave wave,
        KeyLoadClient source, KeyLoadClient target, KeyLoadClient denied, string principal,
        ProtectedDocumentRf3Seed seed, CommandRequest command, long before, CancellationToken token)
    {
        var controls = wave.QueryControls;
        var fault = owner.Arm(principal, command.CommandId, RequestCqrsProbePhase.ControlledDocumentGrantSettled,
            RequestCqrsProbeAction.ThrowOrdinary);
        var first = owner.Own(source.CommitAsync(command, token));
        var marker = await controls.WaitForMarkerAsync(fault, RequestCqrsProbePhase.ControlledDocumentGrantSettled,
            RequestCqrsProbeOutcome.FaultRequested, owner.Discovery, token).ConfigureAwait(false);
        await Assert.That(marker.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(marker.RequestId).IsNotEqualTo(Guid.Empty);
        var uncertain = await first.ConfigureAwait(false);
        await Assert.That(uncertain.IsSuccess).IsFalse();
        await Assert.That(uncertain.Value).IsNull();
        await Assert.That(uncertain.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(uncertain.Problem?.Detail).IsEqualTo(
            "The database request could not complete. Retry the same command ID for writes.");
        var disposed = await controls.WaitForMarkerAsync(fault, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, owner.Discovery, token).ConfigureAwait(false);
        await Assert.That(disposed.RequestId).IsEqualTo(marker.RequestId);
        await Assert.That(disposed.CommandId).IsEqualTo(command.CommandId);
        await controls.RetireArmAsync(fault, token).ConfigureAwait(false);
        var faultCut = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await Assert.That(faultCut.Incarnation).IsEqualTo(seed.Peer.Target.Incarnation);
        await Assert.That(faultCut.Applied).IsGreaterThan(before);
        await ProtectedDocumentUncertaintyOracle.DocumentAsync(source, seed, token).ConfigureAwait(false);
        await NegativesAsync(source, target, denied, seed, command, token).ConfigureAwait(false);

        var outcome = owner.Arm(principal, command.CommandId, RequestCqrsProbePhase.ControlledDocumentOutcomeReturned,
            RequestCqrsProbeAction.Hold);
        var retry = owner.Own(source.CommitAsync(command, token));
        var returned = await controls.WaitForMarkerAsync(outcome, RequestCqrsProbePhase.ControlledDocumentOutcomeReturned,
            RequestCqrsProbeOutcome.Observed, owner.Discovery, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(returned, outcome, command.CommandId,
            RequestCqrsProbePhase.ControlledDocumentOutcomeReturned, owner.Discovery).ConfigureAwait(false);
        await Assert.That(returned.RequestId).IsNotEqualTo(marker.RequestId);
        await Assert.That(retry.IsCompleted).IsFalse();
        var observedCut = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await Assert.That(observedCut.Incarnation).IsEqualTo(seed.Peer.Target.Incarnation);
        await Assert.That(observedCut.Applied).IsGreaterThanOrEqualTo(faultCut.Applied);
        controls.WriteRelease(outcome, returned.RequestId);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await retry.ConfigureAwait(false));
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, outcome, returned.RequestId,
            command.CommandId, owner.Discovery, token).ConfigureAwait(false);
        await controls.RetireArmAsync(outcome, token).ConfigureAwait(false);
        await Assert.That(receipt.Token.Position).IsGreaterThan(before);
        await Assert.That(receipt.Token.Position).IsLessThanOrEqualTo(faultCut.Applied);
        var expected = ProtectedDocumentUncertaintyOracle.Receipt(seed.Partition,
            seed.Peer.Target.Incarnation, receipt.Token.Position);
        await SqlRf3Protocol.EqualAsync(expected, receipt);
        await ProtectedDocumentUncertaintyOracle.DocumentAsync(source, seed, token, expected.Token).ConfigureAwait(false);
        var completedCut = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await Assert.That(completedCut.Incarnation).IsEqualTo(seed.Peer.Target.Incarnation);
        await Assert.That(completedCut.Applied).IsGreaterThanOrEqualTo(observedCut.Applied);
        return expected;
    }

    private static async Task NegativesAsync(KeyLoadClient source, KeyLoadClient target, KeyLoadClient denied,
        ProtectedDocumentRf3Seed seed, CommandRequest command, CancellationToken token)
    {
        var beforeA = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var beforeB = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        var changed = command with
        {
            Mutations = [new PutDocument("protected-documents", "one",
            "{\"title\":\"different\"}", 1, ExplicitReplacement: true)]
        };
        var conflict = await source.CommitAsync(changed, token).ConfigureAwait(false);
        await Assert.That(conflict.IsSuccess).IsFalse();
        await Assert.That(conflict.Value).IsNull();
        await Assert.That(conflict.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await Assert.That(conflict.Problem?.Detail).IsEqualTo("The command ID was already used with different content.");
        await UnchangedAsync(source, target, seed, beforeA.Applied, beforeB.Applied, token).ConfigureAwait(false);
        var rejection = await denied.CommitAsync(command, token).ConfigureAwait(false);
        await Assert.That(rejection.IsSuccess).IsFalse();
        await Assert.That(rejection.Value).IsNull();
        await Assert.That(rejection.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await UnchangedAsync(source, target, seed, beforeA.Applied, beforeB.Applied, token).ConfigureAwait(false);
    }

    private static async Task UnchangedAsync(KeyLoadClient source, KeyLoadClient target,
        ProtectedDocumentRf3Seed seed, long a, long b, CancellationToken token)
    {
        await ProtectedDocumentUncertaintyOracle.DocumentAsync(source, seed, token).ConfigureAwait(false);
        var afterA = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var afterB = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await Assert.That(afterA.Incarnation).IsEqualTo(seed.Peer.Source.Incarnation);
        await Assert.That(afterB.Incarnation).IsEqualTo(seed.Peer.Target.Incarnation);
        await Assert.That(afterA.Applied).IsGreaterThanOrEqualTo(a);
        await Assert.That(afterB.Applied).IsGreaterThanOrEqualTo(b);
    }
}
