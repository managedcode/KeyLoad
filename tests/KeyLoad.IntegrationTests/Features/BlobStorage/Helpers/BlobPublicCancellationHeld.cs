using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPublicCancellationHeld
{
    internal static async Task RunAsync<T>(BlobPublicCancellationOwner owner, bool useMcp, Guid commandId,
        Func<CancellationToken, Task<Result<T>>> sdk, Func<CancellationToken, Task<RequestCqrsFaultMcpObservation>> mcp,
        Func<Task> unchanged, CancellationToken token)
    {
        var arm = owner.Controls.WriteArm(owner.Identity.PrincipalId, commandId, null,
            RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbeAction.Hold);
        owner.Arms.Add(arm);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        owner.CallCancellations.Add(cancellation);
        owner.Lifecycle.SetTokens(cancellation.Token, owner.ParentToken, token);
        Task<Result<T>>? sdkTask = null;
        Task<RequestCqrsFaultMcpObservation>? mcpTask = null;
        if (useMcp)
        {
            mcpTask = mcp(cancellation.Token);
            owner.McpCall = mcpTask;
            owner.SdkCall = null;
        }
        else
        {
            sdkTask = sdk(cancellation.Token);
            owner.SdkCall = sdkTask;
            owner.McpCall = null;
        }
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultMarkerWait);
        var marker = await owner.Controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.BeforeSubmit,
            RequestCqrsProbeOutcome.Observed, owner.Discovery, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, commandId,
            RequestCqrsProbePhase.BeforeSubmit, owner.Discovery);
        await unchanged();
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultCallerCancel);
        await cancellation.CancelAsync();
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProducerSettlement);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(owner.Controls, arm, marker.RequestId,
            commandId, owner.Discovery, token);
        if (useMcp)
        {
            var actualRequest = await RequestCqrsFaultOutcome.AssertMcpInterruptionAsync(await mcpTask!);
            if (actualRequest is { } value)
            {
                await Assert.That(value).IsEqualTo(marker.RequestId);
            }
        }
        else
        {
            var result = await sdkTask!;
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        }
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await unchanged();
        await owner.Controls.RetireArmAsync(arm, token);
    }

    internal static async Task<RequestCqrsFaultMcpObservation> CallAsync<T>(RequestCqrsRf3Callers callers,
        string tool, T request, CancellationToken token)
    {
        try
        {
            return new(await callers.Mcp.CallAsync(tool, request, token), null);
        }
        catch (OperationCanceledException error) { return new(null, error); }
        catch (HttpRequestException error) { return new(null, error); }
        catch (IOException error) { return new(null, error); }
    }
}
