using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedSearchRf3EpochTrial
{
    internal const long ChangedEpoch = 4;
    internal const long HealthyEpoch = 5;
    private const int FirstVoter = 0;
    private const string MissingProblem = "The original distributed statistics refusal has no problem.";
    private const string ChangedDetail = "The distributed search statistics cut or authority changed.";
    private readonly List<Exception> failures = [];
    private Task<Result<DistributedSearchPageV1>>? sdk;
    private Task<CallToolResult>? mcp;
    private Guid arm;
    private Guid requestId;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, KeyLoadClient source,
        KeyLoadClient destination, KeyLoadClient reader, McpOfficialClient official,
        DistributedSearchRf3Seed seed, bool useMcp, CancellationToken token)
    {
        var owner = new DistributedSearchRf3EpochTrial();
        var discovery = new ReplicaSiloDiscovery[TwoRf3MembershipProtocol.MembersPerGroup];
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = FirstVoter; index < discovery.Length; index++)
            {
                discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                    RequestCqrsRf3Protocol.NodeName(index), wave.Profile, token).ConfigureAwait(false);
            }
            await owner.ExecuteAsync(wave.QueryControls, discovery, source, destination,
                reader, official, seed, useMcp, token).ConfigureAwait(false);
        }, owner.failures).ConfigureAwait(false);
        await owner.CleanupAsync(wave.QueryControls, discovery).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(owner.failures);
        await DistributedSearchRf3Seed.ConfigureAsync(destination, seed.Original.Destination.Principal,
            HealthyEpoch, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.HealthyAsync(source, destination, reader, official,
            seed, HealthyEpoch, token).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(RequestCqrsProbeFixture controls,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, DistributedSearchRf3Seed seed,
        bool useMcp, CancellationToken token)
    {
        arm = controls.WriteArm(seed.Original.Destination.Principal.Id, Guid.Empty,
            GrainReadKind.DistributedSearch, RequestCqrsProbePhase.DistributedSearchStatisticsCaptured,
            RequestCqrsProbeAction.Hold);
        if (useMcp)
        { mcp = official.CallAsync(DistributedSearchProtocol.Tool, DistributedSearchRf3Seed.Request(), token); }
        else
        { sdk = reader.DistributedSearchAsync(DistributedSearchRf3Seed.Request(), token); }
        var marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.DistributedSearchStatisticsCaptured,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        requestId = marker.RequestId;
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, Guid.Empty,
            RequestCqrsProbePhase.DistributedSearchStatisticsCaptured, discovery).ConfigureAwait(false);
        await Assert.That(useMcp ? mcp!.IsCompleted : sdk!.IsCompleted).IsFalse();
        await DistributedSearchRf3Seed.ConfigureAsync(destination, seed.Original.Destination.Principal,
            ChangedEpoch, token).ConfigureAwait(false);
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        controls.WriteRelease(arm, requestId);
        await OriginalAsync(useMcp).ConfigureAwait(false);
        await DisposedAsync(controls, discovery, token).ConfigureAwait(false);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
        await controls.RetireArmAsync(arm, token).ConfigureAwait(false);
    }

    private async Task OriginalAsync(bool useMcp)
    {
        if (useMcp)
        {
            await McpCallerAssertions.ErrorAsync(await mcp!.ConfigureAwait(false), ErrorCode.OwnershipLost, dispatched: true);
            return;
        }
        var result = await sdk!.ConfigureAwait(false);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        var problem = result.Problem ?? throw new InvalidOperationException(MissingProblem);
        await Assert.That(problem.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
        await Assert.That(problem.Detail).IsEqualTo(ChangedDetail);
    }

    private async Task DisposedAsync(RequestCqrsProbeFixture controls,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        var marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        await Assert.That(marker.RequestId).IsEqualTo(requestId);
        await Assert.That(controls.ArmFor(arm).ProducerDisposedSeen).IsTrue();
    }

    private async Task CleanupAsync(RequestCqrsProbeFixture controls, IReadOnlyList<ReplicaSiloDiscovery> discovery)
    {
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        if (discovery.All(value => value is not null))
        { await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures).ConfigureAwait(false); }
        if (sdk is { } actualSdk)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await actualSdk.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (mcp is { } actualMcp)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await actualMcp.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (requestId != Guid.Empty && discovery.All(value => value is not null))
        { await ServerFailureObserver.ObserveAsync(() => DisposedAsync(controls, discovery, cleanup.Token), failures).ConfigureAwait(false); }
    }
}
