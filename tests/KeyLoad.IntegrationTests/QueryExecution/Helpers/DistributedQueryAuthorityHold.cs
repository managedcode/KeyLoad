using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedQueryAuthorityHold
{
    private const string McpOwnerChanged = "The database operation could not be completed.";
    private const string SourceOwnerChanged = "The remote partition query authority changed.";
    private readonly List<Exception> failures = [];
    private Task<Result<PartitionQueryPageV1>>? sdk;
    private Task<CallToolResult>? mcp;
    private Guid arm;
    private RequestCqrsProbeMarkerRecord? held;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, DistributedQueryAuthorityContext context,
        DistributedQueryAuthorityState state, bool useMcp, CancellationToken token)
    {
        var owner = new DistributedQueryAuthorityHold();
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var discovery = new List<ReplicaSiloDiscovery>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = DistributedQueryAuthorityProtocol.FirstVoter; index < TwoRf3MembershipProtocol.MembersPerGroup; index++)
            {
                discovery.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                    RequestCqrsRf3Protocol.NodeName(index), wave.Profile, token));
            }
            await owner.ExecuteAsync(wave.QueryControls, discovery, context, state, useMcp, selected.Token);
        }, owner.failures);
        await owner.CleanupAsync(wave.QueryControls, discovery, selected);
        ServerFailureObserver.ThrowIfAny(owner.failures);
    }

    private async Task ExecuteAsync(RequestCqrsProbeFixture controls, List<ReplicaSiloDiscovery> discovery,
        DistributedQueryAuthorityContext context, DistributedQueryAuthorityState state, bool useMcp, CancellationToken token)
    {
        arm = controls.WriteArm(state.Seed.Destination.Principal.Id, Guid.Empty, GrainReadKind.PartitionQuery,
            RequestCqrsProbePhase.NativeModelAfterDispatch, RequestCqrsProbeAction.Hold);
        if (useMcp)
        {
            mcp = context.Official.CallAsync(RemotePartitionQueryRf3Seed.Tool, RemotePartitionQueryRf3Seed.Request(), token);
        }
        else
        {
            sdk = context.Reader.PartitionQueryAsync(RemotePartitionQueryRf3Seed.Request(), token);
        }
        var marker = await QueryLifetimeNativeHoldBoundary.WaitAsync(controls, arm, discovery,
            useMcp ? mcp! : sdk!, () => ObserveEarlyAsync(useMcp), token);
        held = marker;
        await QueryLifetimeNativeWitness.RequireAsync(controls, marker, RemoteResourceLifetimeReadKindV2.PartitionQuery,
            state.Seed.Destination.Principal.Id);
        await DistributedQueryAuthorityPolicy.ResourceAsync(context.Source, DistributedQueryAuthorityProtocol.ChangedSchema, true, token);
        state.SourceSchema = DistributedQueryAuthorityProtocol.ChangedSchema;
        await DistributedQueryAuthorityCurrent.ObserveAsync(context, state, async () =>
        {
            controls.WriteRelease(arm, marker.RequestId);
            if (useMcp)
            {
                await DistributedQueryAuthorityProblems.McpAsync(await mcp!, ErrorCode.OwnershipLost, McpOwnerChanged);
            }
            else
            {
                await DistributedQueryAuthorityProblems.SdkAsync(await sdk!, ErrorCode.OwnershipLost, SourceOwnerChanged);
            }
        }, token);
    }

    private async Task ObserveEarlyAsync(bool useMcp)
    {
        if (useMcp)
        {
            _ = await McpCallerAssertions.SuccessAsync<PartitionQueryPageV1>(await mcp!);
        }
        else
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await sdk!);
        }
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
        if (sdk is { } originalSdk)
        {
            await ServerFailureObserver.ObserveAsync(async () => { _ = await originalSdk; }, failures);
        }
        if (mcp is { } originalMcp)
        {
            await ServerFailureObserver.ObserveAsync(async () => { _ = await originalMcp; }, failures);
        }
        if (held is { } marker && discovery.Count == TwoRf3MembershipProtocol.MembersPerGroup)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, arm, marker.RequestId,
                    Guid.Empty, discovery, cleanup.Token);
                await controls.RetireArmAsync(arm, cleanup.Token);
            }, failures);
        }
    }
}
