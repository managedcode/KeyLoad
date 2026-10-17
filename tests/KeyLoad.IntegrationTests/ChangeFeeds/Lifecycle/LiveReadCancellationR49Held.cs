using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadCancellationR49Held
{
    internal static async Task RunAsync(LiveReadCancellationR49Owner owner, bool useMcp, CancellationToken token)
    {
        var arm = owner.Controls.WriteArm(owner.Identity.PrincipalId, Guid.Empty, GrainReadKind.LiveQueryRead,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        owner.Arms.Add(arm);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        owner.Cancellations.Add(cancellation);
        if (useMcp)
        {
            owner.McpCall = OfficialAsync(owner, cancellation.Token);
        }
        else
        {
            owner.SdkCall = owner.Caller.Sdk.ReadLiveQueryAsync(owner.Original, cancellation.Token);
        }
        var marker = await owner.Controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, owner.Discovery, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, owner.Discovery);
        await Assert.That(useMcp ? owner.McpCall!.IsCompleted : owner.SdkCall!.IsCompleted).IsFalse();
        await cancellation.CancelAsync();
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(owner.Controls, arm, marker.RequestId,
            Guid.Empty, owner.Discovery, token);
        if (useMcp)
        {
            var observed = await owner.McpCall!;
            await Assert.That(observed.ToolResult).IsNull();
            await Assert.That(observed.TransportFailure is OperationCanceledException or IOException
                or HttpRequestException { StatusCode: null }).IsTrue();
        }
        else
        {
            var observed = await owner.SdkCall!;
            await Assert.That(observed.IsSuccess).IsFalse();
            await Assert.That(observed.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled,
                LiveReadCancellationR49Protocol.ReadUnavailable), observed.Problem);
        }
        await owner.Controls.RetireArmAsync(arm, token);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        await LiveReadCancellationR49Pages.HealthyAsync(owner, owner.Original, false, token);
    }

    private static async Task<RequestCqrsFaultMcpObservation> OfficialAsync(LiveReadCancellationR49Owner owner,
        CancellationToken token)
    {
        try
        {
            return new(await owner.Caller.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, owner.Original, token), null);
        }
        catch (OperationCanceledException error) { return new(null, error); }
        catch (HttpRequestException error) { return new(null, error); }
        catch (IOException error) { return new(null, error); }
    }
}
