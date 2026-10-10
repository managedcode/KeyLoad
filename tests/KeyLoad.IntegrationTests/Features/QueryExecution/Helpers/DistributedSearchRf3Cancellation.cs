using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedSearchRf3Cancellation
{
    private const int FirstVoterIndex = 0;
    private const int NoFailures = 0;
    private const string Missing = "The original distributed search cancellation owner is unavailable.";
    private const string ReadUnavailable = "The read response is unavailable.";
    private readonly List<Exception> failures = [];
    private CancellationTokenSource? caller;
    private Task<Result<DistributedSearchPageV1>>? sdk;
    private Task<RequestCqrsFaultMcpObservation>? mcp;
    private Guid arm;
    private Guid parentArm;
    private Guid parentRequest;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, KeyLoadClient reader, McpOfficialClient official,
        DistributedSearchRf3Seed seed, bool useMcp, CancellationToken token)
    {
        var owner = new DistributedSearchRf3Cancellation();
        var discovery = new ReplicaSiloDiscovery[TwoRf3MembershipProtocol.MembersPerGroup];
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = FirstVoterIndex; index < discovery.Length; index++)
            {
                discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                RequestCqrsRf3Protocol.NodeName(index), wave.Profile, token).ConfigureAwait(false);
            }
            await owner.ExecuteAsync(wave.QueryControls, discovery, reader, official, seed, useMcp, token).ConfigureAwait(false);
        }, owner.failures).ConfigureAwait(false);
        await owner.CleanupAsync(wave.QueryControls, discovery).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(owner.failures);
    }

    private async Task ExecuteAsync(RequestCqrsProbeFixture controls, IReadOnlyList<ReplicaSiloDiscovery> discovery,
        KeyLoadClient reader, McpOfficialClient official, DistributedSearchRf3Seed seed, bool useMcp, CancellationToken token)
    {
        parentArm = controls.WriteArm(seed.Original.Destination.Principal.Id, Guid.Empty, GrainReadKind.DistributedSearch,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        arm = controls.WriteArm(seed.Original.Destination.Principal.Id, Guid.Empty, GrainReadKind.DistributedSearchLeaf,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold, RemotePartitionQueryRf3Seed.Local);
        caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (useMcp)
        { mcp = CallMcpAsync(official, caller.Token); }
        else
        { sdk = reader.DistributedSearchAsync(DistributedSearchRf3Seed.Request(), caller.Token); }
        var parent = await controls.WaitForMarkerAsync(parentArm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        parentRequest = parent.RequestId;
        controls.WriteRelease(parentArm, parentRequest);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(parent, parentArm, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, discovery).ConfigureAwait(false);
        var marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, discovery).ConfigureAwait(false);
        await Assert.That(marker.RequestId).IsNotEqualTo(parentRequest);
        token.ThrowIfCancellationRequested();
        await Assert.That(useMcp ? mcp!.IsCompleted : sdk!.IsCompleted).IsFalse();
        await caller.CancelAsync().ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, arm, marker.RequestId,
            Guid.Empty, discovery, token).ConfigureAwait(false);
        await ParentDisposedAsync(controls, discovery, token).ConfigureAwait(false);
        await OriginalAsync(useMcp).ConfigureAwait(false);
        await controls.RetireArmAsync(arm, token).ConfigureAwait(false);
        await controls.RetireArmAsync(parentArm, token).ConfigureAwait(false);
    }

    private async Task ParentDisposedAsync(RequestCqrsProbeFixture controls,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        var settled = await controls.WaitForMarkerAsync(parentArm, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        await Assert.That(settled.RequestId).IsEqualTo(parentRequest);
        await Assert.That(settled.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(controls.ArmFor(parentArm).ProducerDisposedSeen).IsTrue();
    }

    private async Task OriginalAsync(bool useMcp)
    {
        await Assert.That(caller?.IsCancellationRequested).IsTrue();
        if (useMcp)
        {
            var actual = await (mcp ?? throw new InvalidOperationException(Missing)).ConfigureAwait(false);
            await Assert.That(actual.ToolResult).IsNull();
            await Assert.That(actual.TransportFailure is OperationCanceledException or IOException
                or HttpRequestException { StatusCode: null }).IsTrue();
            return;
        }
        var reply = await (sdk ?? throw new InvalidOperationException(Missing)).ConfigureAwait(false);
        await Assert.That(reply.IsSuccess).IsFalse();
        await Assert.That(reply.Value).IsNull();
        await Assert.That(reply.Problem?.ErrorCode).IsEqualTo(ErrorCode.Cancelled.ToString());
        await Assert.That(reply.Problem?.Detail).IsEqualTo(ReadUnavailable);
    }

    private async Task CleanupAsync(RequestCqrsProbeFixture controls, IReadOnlyList<ReplicaSiloDiscovery> discovery)
    {
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        if (caller is { } actualCaller)
        { await ServerFailureObserver.ObserveAsync(actualCaller.CancelAsync, failures).ConfigureAwait(false); }
        if (failures.Count != NoFailures && discovery.All(value => value is not null))
        { await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures).ConfigureAwait(false); }
        if (sdk is { } actualSdk)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await actualSdk.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (mcp is { } actualMcp)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await actualMcp.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (parentRequest != Guid.Empty && discovery.All(value => value is not null))
        { await ServerFailureObserver.ObserveAsync(() => ParentDisposedAsync(controls, discovery, cleanup.Token), failures).ConfigureAwait(false); }
        if (caller is { } disposedCaller)
        { ServerFailureObserver.Observe(disposedCaller.Dispose, failures); }
    }

    private static async Task<RequestCqrsFaultMcpObservation> CallMcpAsync(McpOfficialClient official, CancellationToken token)
    {
        try
        { return new(await official.CallAsync(DistributedSearchProtocol.Tool, DistributedSearchRf3Seed.Request(), token).ConfigureAwait(false), null); }
        catch (OperationCanceledException error) { return new(null, error); }
        catch (HttpRequestException error) { return new(null, error); }
        catch (IOException error) { return new(null, error); }
    }
}
