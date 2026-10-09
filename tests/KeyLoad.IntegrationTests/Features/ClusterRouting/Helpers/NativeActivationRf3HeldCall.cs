using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationRf3HeldCall
{
    internal static async Task<(RequestCqrsProbeActivationRecord Witness, CommitReceipt Receipt)> ExecuteAsync(
        TwoRf3MembershipWave wave, RequestCqrsRf3Callers callers, RequestCqrsPhaseFaultIdentity identity,
        CommandRequest command, bool useMcp, IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken cancellationToken)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var failures = new List<Exception>();
        Task<CommitReceipt>? producer = null;
        RequestCqrsProbeMarkerRecord? observed = null;
        (RequestCqrsProbeActivationRecord Witness, CommitReceipt Receipt)? result = null;
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(identity.PrincipalId, command.CommandId, null,
            RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbeAction.Hold);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            producer = SendAsync(callers, command, useMcp, caller.Token);
            var marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.BeforeSubmit,
                RequestCqrsProbeOutcome.Observed, discovery, cancellationToken).ConfigureAwait(false);
            observed = marker;
            await Assert.That(producer.IsCompleted).IsFalse();
            var witness = await NativeActivationRf3Witness.ReadAsync(controls, marker, discovery,
                cancellationToken).ConfigureAwait(false);
            controls.WriteRelease(arm, marker.RequestId);
            var receipt = await producer.ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, arm, marker.RequestId,
                command.CommandId, discovery, cancellationToken).ConfigureAwait(false);
            await controls.RetireArmAsync(arm, cancellationToken).ConfigureAwait(false);
            result = (witness, receipt);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(caller.Cancel, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        if (producer is { } original)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await original.WaitAsync(cleanup.Token).ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (result is null)
        { await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures).ConfigureAwait(false); }
        if (result is null && observed is { } originalMarker)
        {
            await ServerFailureObserver.ObserveAsync(() => RequestCqrsPhaseFaultAssertions.VerifySettledAsync(
            controls, arm, originalMarker.RequestId, command.CommandId, discovery, cleanup.Token), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(NativeActivationRf3Protocol.Missing);
    }

    private static async Task<CommitReceipt> SendAsync(RequestCqrsRf3Callers callers, CommandRequest command,
        bool useMcp, CancellationToken cancellationToken)
        => useMcp
            ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false)).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command,
                cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
}
