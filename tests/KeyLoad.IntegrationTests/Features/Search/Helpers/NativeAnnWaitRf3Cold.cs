using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnWaitRf3Cold
{
    private const string Scenario = "ann-wait-cold-owner";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, string readerSecret,
        NativeTextRf3Scenario scenario, AnnMaintenanceRequest pin, WaitForAnnIndexRequest request,
        CommandRequest original, CommitReceipt receipt, NodeStatus beforeStop, CancellationToken token)
    {
        var stopped = await Task.WhenAll(Nodes.Select(node => ObserveAsync(
            () => fixture.KillContainerAsync(node, Scenario, token))));
        var restarted = await Task.WhenAll(Nodes.Select(node => ObserveAsync(
            () => fixture.RestartContainerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. stopped.SelectMany(row => row), .. restarted.SelectMany(row => row)]);
        foreach (var node in Nodes)
        { await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var administrator = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var recovered = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
                await Assert.That(recovered.NodeId).IsEqualTo(beforeStop.NodeId);
                await Assert.That(recovered.Incarnation).IsEqualTo(beforeStop.Incarnation);
                await Assert.That(recovered.ReadGeneration).IsGreaterThanOrEqualTo(beforeStop.ReadGeneration);
                await Assert.That(recovered.Applied).IsGreaterThanOrEqualTo(beforeStop.Applied);
                await using var reader = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, readerSecret, token);
                await ServerFailureObserver.ObserveAsync(async () =>
                { await ContinueAsync(administrator, reader, scenario, pin, request, original, receipt, token); }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ContinueAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        NativeTextRf3Scenario scenario, AnnMaintenanceRequest pin, WaitForAnnIndexRequest request,
        CommandRequest original, CommitReceipt receipt, CancellationToken token)
    {
        await NativeAnnWaitRf3Assertions.RejectedAsync(administrator.Sdk, reader, request, ErrorCode.HistoryUnavailable, token);
        var restored = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.MaintainAnnIndexAsync(
            pin with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore }, token));
        await NativeAnnWaitRf3Assertions.AllPathsAsync(reader, request, restored.Source!.AppliedPosition, token);
        await NativeAnnWaitRf3Assertions.LiteralAsync(reader, scenario, pin, true, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original, token)));
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
        await Assert.That(after.Applied).IsGreaterThan(before.Applied);
        await NativeAnnWaitRf3Assertions.LiteralAsync(reader, scenario, pin, true, token);
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}
