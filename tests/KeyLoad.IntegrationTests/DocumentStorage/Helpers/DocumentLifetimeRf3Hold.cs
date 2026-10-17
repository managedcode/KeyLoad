using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed class DocumentLifetimeRf3Hold(RequestCqrsProbeFixture fixture, string principal)
{
    private readonly Guid arm = fixture.WriteArm(principal, Guid.Empty, GrainReadKind.Document,
        RequestCqrsProbePhase.NativeModelAfterDispatch, RequestCqrsProbeAction.Hold);
    private RequestCqrsProbeMarkerRecord? marker;

    internal async Task WaitAsync(Task<Result<DocumentReadResult>> original,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var observed = fixture.WaitForMarkerAsync(arm, RequestCqrsProbePhase.NativeModelAfterDispatch,
            RequestCqrsProbeOutcome.Observed, discovery, waiting.Token);
        if (await Task.WhenAny(original, observed).ConfigureAwait(false) == original)
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await McpCallerAssertions.SdkSuccessAsync(await original.ConfigureAwait(false));
            }, failures);
            await ServerFailureObserver.ObserveAsync(waiting.CancelAsync, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    _ = await observed.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (waiting.IsCancellationRequested && !token.IsCancellationRequested) { }
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException(RemoteDocumentRf3Protocol.DocumentsGet);
        }
        marker = await observed.ConfigureAwait(false);
    }

    internal void Release()
    {
        if (marker is { } actual)
        {
            fixture.WriteRelease(arm, actual.RequestId);
        }
    }

    internal async Task JoinAsync(IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        if (marker is not null)
        {
            await fixture.WaitForSettlementAsync(arm, discovery, token).ConfigureAwait(false);
        }
        await fixture.RetireArmAsync(arm, token).ConfigureAwait(false);
    }
}
