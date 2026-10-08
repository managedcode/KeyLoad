using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Cancellation is triggered after an actual signed producer observation, and every original task is joined.</summary>
internal static class ProtectedDocumentRf3Cancellation
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, KeyLoadClient reader,
        McpOfficialClient official, ProtectedDocumentRf3Seed seed, bool useMcp, CancellationToken token)
    {
        var failures = new List<Exception>();
        var discovery = new ReplicaSiloDiscovery[3];
        for (var index = 0; index < discovery.Length; index++)
        {
            discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                RequestCqrsRf3Protocol.NodeName(index), wave.Profile, token).ConfigureAwait(false);
        }
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(seed.ReaderId, Guid.Empty, GrainReadKind.Document,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<Result<DocumentResult?>>? sdk = null;
        Task<RequestCqrsFaultMcpObservation>? mcp = null;
        if (useMcp)
        { mcp = CallAsync(official, seed, caller.Token); }
        else
        { sdk = reader.GetAsync(seed.Reference, caller.Token); }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
                RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, Guid.Empty,
                RequestCqrsProbePhase.AuthorizationReload, discovery).ConfigureAwait(false);
            await Assert.That(useMcp ? mcp!.IsCompleted : sdk!.IsCompleted).IsFalse();
            await caller.CancelAsync().ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, arm, marker.RequestId,
                Guid.Empty, discovery, token).ConfigureAwait(false);
            if (useMcp)
            {
                var result = await mcp!.ConfigureAwait(false);
                await Assert.That(result.ToolResult).IsNull();
                await Assert.That(result.TransportFailure is OperationCanceledException or IOException
                    or HttpRequestException { StatusCode: null }).IsTrue();
            }
            else
            {
                var result = await sdk!.ConfigureAwait(false);
                await Assert.That(result.IsSuccess).IsFalse();
                await Assert.That(result.Value).IsNull();
                await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Cancelled));
            }
            await controls.RetireArmAsync(arm, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        if (!controls.ArmFor(arm).Retired)
        { await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures).ConfigureAwait(false); }
        if (sdk is { } sdkTask)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await sdkTask.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (mcp is { } mcpTask)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await mcpTask.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<RequestCqrsFaultMcpObservation> CallAsync(McpOfficialClient official,
        ProtectedDocumentRf3Seed seed, CancellationToken token)
    {
        try
        { return new(await official.CallAsync("keyload_documents_get", new GetDocumentRequest(seed.Reference), token).ConfigureAwait(false), null); }
        catch (OperationCanceledException error) { return new(null, error); }
        catch (HttpRequestException error) { return new(null, error); }
        catch (IOException error) { return new(null, error); }
    }
}
