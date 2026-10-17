using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class AnnLifetimeRf3Hold
{
    private readonly List<Exception> failures = [];
    private Task? producer;
    private bool originalOutcomeValidated;
    private Guid arm;
    private RequestCqrsProbeMarkerRecord? marker;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, RemoteAnnPublicationRf3State state, PrincipalRecord changed,
        bool wait, bool mcp, bool cancel, CancellationToken token)
    {
        var owner = new AnnLifetimeRf3Hold();
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var discovery = new List<ReplicaSiloDiscovery>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = 0; index < TwoRf3MembershipProtocol.MembersPerGroup; index++)
            {
                discovery.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                    RequestCqrsRf3Protocol.NodeName(index), wave.Profile, token));
            }
            if (wait)
            {
                await owner.ExecuteAsync(wave.QueryControls, administrator, reader, state, changed, mcp, cancel,
                    GrainReadKind.WaitForAnnIndex, WaitForAnnIndexProtocol.Tool, state.Wait(),
                    ct => reader.Sdk.WaitForAnnIndexAsync(state.Wait(), ct), discovery, selected, token);
            }
            else
            {
                await owner.ExecuteAsync(wave.QueryControls, administrator, reader, state, changed, mcp, cancel,
                    GrainReadKind.ApproximateSearch, AnnSearchProtocol.Tool, state.Approximate(),
                    ct => reader.Sdk.ApproximateSearchAsync(state.Approximate(), ct), discovery, selected, token);
            }
        }, owner.failures);
        await owner.CleanupAsync(wave.QueryControls, discovery, selected);
        ServerFailureObserver.ThrowIfAny(owner.failures);
    }

    private async Task ExecuteAsync<T>(RequestCqrsProbeFixture controls, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, RemoteAnnPublicationRf3State state, PrincipalRecord changed, bool useMcp, bool cancel,
        GrainReadKind kind, string tool, object request, Func<CancellationToken, Task<Result<T>>> call,
        List<ReplicaSiloDiscovery> discovery, CancellationTokenSource selected, CancellationToken token)
    {
        arm = controls.WriteArm(changed.Id, Guid.Empty, kind, RequestCqrsProbePhase.NativeModelAfterDispatch,
            RequestCqrsProbeAction.Hold);
        var sdk = useMcp ? null : call(selected.Token);
        var mcp = useMcp ? reader.Mcp.CallAsync(tool, request, selected.Token) : null;
        producer = useMcp ? mcp : sdk;
        var observed = await QueryLifetimeNativeHoldBoundary.WaitAsync(controls, arm, discovery,
            producer ?? throw new InvalidOperationException(AnnLifetimeRf3Assertions.MissingProducer), async () =>
            {
                if (useMcp)
                {
                    _ = await McpCallerAssertions.SuccessAsync<T>(await mcp!);
                }
                else
                {
                    _ = await McpCallerAssertions.SdkSuccessAsync(await sdk!);
                }
            }, token);
        marker = observed;
        await AnnLifetimeRf3Witness.RequireAsync(controls, observed, state, changed.Id, kind);
        if (!cancel)
        {
            await RemoteAnnPublicationRf3Assertions.EqualAsync(changed,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), changed, token)));
        }
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        if (cancel)
        {
            await selected.CancelAsync();
        }
        else
        {
            controls.WriteRelease(arm, observed.RequestId);
        }
        await AnnLifetimeRf3Assertions.OriginalAsync(sdk, mcp, selected, cancel);
        originalOutcomeValidated = true;
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, arm, observed.RequestId, Guid.Empty, discovery, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
    }

    private async Task CleanupAsync(RequestCqrsProbeFixture controls, List<ReplicaSiloDiscovery> discovery,
        CancellationTokenSource selected)
    {
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures);
        if (discovery.Count == TwoRf3MembershipProtocol.MembersPerGroup)
        {
            await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures);
        }
        if (producer is { } original && !originalOutcomeValidated)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures);
        }
        if (marker is { } held && discovery.Count == TwoRf3MembershipProtocol.MembersPerGroup)
        {
            await ServerFailureObserver.ObserveAsync(() => RequestCqrsPhaseFaultAssertions.VerifySettledAsync(
                controls, arm, held.RequestId, Guid.Empty, discovery, cleanup.Token), failures);
            await ServerFailureObserver.ObserveAsync(() => controls.RetireArmAsync(arm, cleanup.Token), failures);
        }
    }
}
