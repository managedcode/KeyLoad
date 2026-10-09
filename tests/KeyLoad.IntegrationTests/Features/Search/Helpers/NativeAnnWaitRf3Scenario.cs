using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnWaitRf3Scenario
{
    private const long Revision = 1;
    private const string Node = McpCallerProtocol.Node1;

    internal static async Task RunAsync(ClusterFixture fixture, List<Exception> failures)
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var scenario = await NativeTextRf3Scenario.CreateAsync(fixture, token);
        var deniedIdentity = await scenario.CreateReaderAsync(fixture, false, false, false, false, token);
        var identity = await scenario.CreateReaderAsync(fixture, false, true, false, false, token);
        await using var administrator = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, Node, fixture.AdminKey, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var pin = (await new NativeAnnMaintenanceRf3Scenario(scenario.Partition)
                .RequestAsync(administrator.Sdk, administrator.Mcp, token)) with
            { Collection = NativeTextRf3Scenario.Collection, Field = NativeTextRf3Scenario.VectorField, Space = NativeTextRf3Scenario.SpaceFor() };
            var built = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.MaintainAnnIndexAsync(pin, token));
            await using var denied = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, Node, deniedIdentity.Secret, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var reader = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, Node, identity.Secret, token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var request = new WaitForAnnIndexRequest(scenario.Partition, NativeTextRf3Scenario.Collection,
                        NativeTextRf3Scenario.VectorField, NativeTextRf3Scenario.SpaceFor(), pin.Consumer, pin.IndexGeneration, scenario.SeedToken);
                    await NativeAnnWaitRf3Assertions.RejectedAsync(administrator.Sdk, denied, request, ErrorCode.PermissionDenied, token);
                    await NativeAnnWaitRf3Assertions.AllPathsAsync(reader, request, built.Source!.AppliedPosition, token);
                    await NativeAnnWaitRf3Assertions.LiteralAsync(reader, scenario, pin, false, token);
                    var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
                        [new PutVector(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId,
                            NativeTextRf3Scenario.VectorField, [2, 0], NativeTextRf3Scenario.SpaceFor(), Revision),
                         new DeleteDocument(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.ThirdId, Revision)]);
                    var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
                    var changed = request with { MinimumToken = receipt.Token };
                    await NativeAnnWaitRf3Assertions.RejectedAsync(administrator.Sdk, reader, changed, ErrorCode.HistoryUnavailable, token);
                    var restored = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.MaintainAnnIndexAsync(
                        pin with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore }, token));
                    await NativeAnnWaitRf3Assertions.AllPathsAsync(reader, changed, restored.Source!.AppliedPosition, token);
                    await NativeAnnWaitRf3Assertions.LiteralAsync(reader, scenario, pin, true, token);
                    var beforeStop = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
                    await NativeAnnWaitRf3Cold.RunAsync(fixture, identity.Secret, scenario, pin, changed, command, receipt, beforeStop, token);
                }, failures);
            }, failures);
        }, failures);
    }
}
