using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadCancellationR45Held
{
    private const string CancelledDetail = "The read response is unavailable.";
    internal static async Task RunAsync(BlobPublicCancellationOwner owner, bool useMcp,
        BlobPartialReadCancellationR45State state, CancellationToken token)
    {
        var request = new BlobReadRequest(state.Publication.Begin.Blob, 1L,
            BlobPartialReadCancellationR45Corpus.BoundaryOffset, BlobPartialReadCancellationR45Corpus.BoundaryCount);
        var arm = owner.Controls.WriteArm(owner.Identity.PrincipalId, Guid.Empty, GrainReadKind.BlobRange,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        owner.Arms.Add(arm);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        owner.CallCancellations.Add(cancellation);
        owner.Lifecycle.SetTokens(cancellation.Token, owner.ParentToken, token);
        Task<Result<BlobReadResult>>? sdkTask = null;
        Task<RequestCqrsFaultMcpObservation>? mcpTask = null;
        if (useMcp)
        {
            mcpTask = BlobPublicCancellationHeld.CallAsync(owner.Caller,
                BlobPartialReadCancellationR45Pages.RangeTool, request, cancellation.Token);
            owner.McpCall = mcpTask;
            owner.SdkCall = null;
        }
        else
        {
            sdkTask = owner.Caller.Sdk.ReadBlobRangeAsync(request, cancellation.Token);
            owner.SdkCall = sdkTask;
            owner.McpCall = null;
        }
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultMarkerWait);
        var marker = await owner.Controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, owner.Discovery, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, owner.Discovery);
        await Assert.That(useMcp ? mcpTask!.IsCompleted : sdkTask!.IsCompleted).IsFalse();
        await BlobPartialReadCancellationR45Pages.CurrentAsync(owner.Administrator, state, token);
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultCallerCancel);
        await cancellation.CancelAsync();
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProducerSettlement);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(owner.Controls, arm, marker.RequestId,
            Guid.Empty, owner.Discovery, token);
        if (useMcp)
        {
            var result = await mcpTask!;
            await Assert.That(result.ToolResult).IsNull();
            await Assert.That(result.TransportFailure is OperationCanceledException or IOException
                or HttpRequestException { StatusCode: null }).IsTrue();
        }
        else
        {
            var result = await sdkTask!;
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, CancelledDetail), result.Problem);
        }
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await BlobPartialReadCancellationR45Pages.CurrentAsync(owner.Administrator, state, token);
        await owner.Controls.RetireArmAsync(arm, token);
    }
}
