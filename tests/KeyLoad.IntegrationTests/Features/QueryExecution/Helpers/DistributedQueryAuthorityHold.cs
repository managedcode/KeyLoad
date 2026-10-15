using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
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
    private Guid parentArm;
    private Guid leafArm;
    private RequestCqrsProbeMarkerRecord? parent;
    private RequestCqrsProbeMarkerRecord? leaf;
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
        var principal = state.Seed.Destination.Principal.Id;
        parentArm = controls.WriteArm(principal, Guid.Empty, GrainReadKind.PartitionQuery,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        leafArm = controls.WriteArm(principal, Guid.Empty, GrainReadKind.PartitionQueryLeaf,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold, RemotePartitionQueryRf3Seed.Local);
        if (useMcp)
        {
            mcp = context.Official.CallAsync(RemotePartitionQueryRf3Seed.Tool, RemotePartitionQueryRf3Seed.Request(), token);
        }
        else
        {
            sdk = context.Reader.PartitionQueryAsync(RemotePartitionQueryRf3Seed.Request(), token);
        }
        var capturedParent = await MarkerAsync(controls, discovery, parentArm, token);
        parent = capturedParent;
        controls.WriteRelease(parentArm, capturedParent.RequestId);
        var capturedLeaf = await MarkerAsync(controls, discovery, leafArm, token);
        leaf = capturedLeaf;
        await Assert.That(capturedParent.RequestId).IsNotEqualTo(capturedLeaf.RequestId);
        await Assert.That(useMcp ? mcp!.IsCompleted : sdk!.IsCompleted).IsFalse();
        await DistributedQueryAuthorityPolicy.ResourceAsync(context.Source, DistributedQueryAuthorityProtocol.ChangedSchema, true, token);
        state.SourceSchema = DistributedQueryAuthorityProtocol.ChangedSchema;
        await DistributedQueryAuthorityCurrent.ObserveAsync(context, state, async () =>
        {
            controls.WriteRelease(leafArm, capturedLeaf.RequestId);
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
    private static async Task<RequestCqrsProbeMarkerRecord> MarkerAsync(RequestCqrsProbeFixture controls,
        List<ReplicaSiloDiscovery> discovery, Guid arm, CancellationToken token)
    {
        var marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, discovery, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, discovery);
        return marker;
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
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await originalSdk;
            }, failures);
        }
        if (mcp is { } originalMcp)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await originalMcp;
            }, failures);
        }
        foreach (var marker in new[]
        {
            parent,
            leaf
        })
        {
            if (marker is { } captured && discovery.Count == TwoRf3MembershipProtocol.MembersPerGroup)
            {
                await ServerFailureObserver.ObserveAsync(() => SettleAsync(controls, discovery, captured, cleanup.Token), failures);
            }
        }
    }
    private static async Task SettleAsync(RequestCqrsProbeFixture controls, List<ReplicaSiloDiscovery> discovery,
        RequestCqrsProbeMarkerRecord marker, CancellationToken token)
    {
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, marker.ArmId, marker.RequestId,
            Guid.Empty, discovery, token);
        await controls.RetireArmAsync(marker.ArmId, token);
    }
}
