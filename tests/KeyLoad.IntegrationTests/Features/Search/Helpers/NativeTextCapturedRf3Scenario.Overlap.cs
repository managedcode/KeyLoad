using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed partial class NativeTextCapturedRf3Scenario
{
    private async Task OverlapAsync(NativeTextMaintenanceRf3Scenario seed,
        OnlineTextIndexMaintenanceRequest request, OnlineTextIndexMaintenanceResult receipt, CancellationToken token)
    {
        var active = publisher ?? throw new InvalidOperationException();
        var probe = controls ?? throw new InvalidOperationException();
        var signed = discovery ?? throw new InvalidOperationException();
        var search = new SearchRequest(seed.Partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Field, "ПРИВІТ");
        arm = probe.WriteArm(publisherId, Guid.Empty, GrainReadKind.Search,
            RequestCqrsProbePhase.NativeTextOriginalPostingRead, RequestCqrsProbeAction.Hold,
            targetVoter: signed[Enumerable.Range(0, RequestCqrsRf3Protocol.NodeCount).Single(index =>
                RequestCqrsRf3Protocol.NodeName(index) == publisherNode)].VoterId);
        originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        originalCall = NativeTextCapturedRf3Call.ExecuteAsync(active, search, path, originalCancellation.Token);
        var marker = await probe.WaitForMarkerAsync(arm, RequestCqrsProbePhase.NativeTextOriginalPostingRead,
            RequestCqrsProbeOutcome.Observed, signed, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, Guid.Empty,
            RequestCqrsProbePhase.NativeTextOriginalPostingRead, signed);
        var successorRequest = request with { CommandId = Guid.NewGuid() };
        capturedArm = probe.WriteArm(publisherId, successorRequest.CommandId, null,
            RequestCqrsProbePhase.OnlineTextCaptured, RequestCqrsProbeAction.Hold,
            targetVoter: signed[Enumerable.Range(0, RequestCqrsRf3Protocol.NodeCount).Single(index =>
                RequestCqrsRf3Protocol.NodeName(index) == publisherNode)].VoterId);
        capturedCall = NativeTextOnlineRf3Call.ExecuteAsync(active.Sdk, active.Mcp, successorRequest, path, token);
        var (mutation, successor) = await NativeTextCapturedRf3BuildWindow.CompleteAsync(probe, signed, active,
            seed, successorRequest, capturedArm, capturedCall, token);
        await Assert.That(successor.PublishedCut.ThroughSequence).IsGreaterThan(receipt.PublishedCut.ThroughSequence);
        if (cancelOriginal)
        { await originalCancellation.CancelAsync(); }
        else
        { probe.WriteRelease(arm, marker.RequestId); }
        var actual = await originalCall;
        if (cancelOriginal)
        { await NativeTextCapturedRf3Call.CancelledAsync(actual, originalCancellation.IsCancellationRequested); }
        else
        { await OriginalAsync(actual, seed.Partition); }
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(probe, arm, marker.RequestId, Guid.Empty, signed, token);
        await probe.RetireArmAsync(arm, token);
        await NativeTextMaintenanceRf3Mutation.ReplayAsync(active.Sdk, active.Mcp, mutation, token);
        await NativeTextOnlineRf3Assertions.ReplayAllAsync(active.Sdk, active.Mcp, request, receipt, token);
        await NativeTextOnlineRf3Assertions.ReplayAllAsync(active.Sdk, active.Mcp, successorRequest, successor, token);
        await NativeTextOnlineRf3Assertions.LiteralAllAsync(active.Sdk, active.Mcp, seed, true, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await active.Sdk.StatusAsync(token));
        await ColdAsync(seed, request, receipt, successorRequest, successor, before, token);
    }

    private static async Task OriginalAsync(NativeTextCapturedRf3Observation actual, PartitionRef partition)
    {
        await Assert.That(actual.TransportFailure).IsNull();
        await Assert.That(actual.SdkError).IsNull();
        await Assert.That(actual.McpError).IsNull();
        RankedDocument[] expected = [new(new(new(partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Ukrainian), 1, NativeTextMaintenanceRf3Scenario.UkrainianJson, false, []), 1d / 61d)];
        await Assert.That(JsonDefaults.Serialize(actual.Rows).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
