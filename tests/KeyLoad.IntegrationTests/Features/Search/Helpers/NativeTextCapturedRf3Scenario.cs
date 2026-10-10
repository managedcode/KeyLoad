using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed partial class NativeTextCapturedRf3Scenario(NativeTextMaintenancePath path, bool cancelOriginal)
{
    private readonly List<Exception> failures = [];
    private string root = string.Empty;
    private bool rootOwned;
    private bool startupAttempted;
    private RequestCqrsProbeFixture? controls;
    private RequestCqrsRf3Wave? wave;
    private RequestCqrsRf3Callers? administrator;
    private RequestCqrsRf3Callers? publisher;
    private IReadOnlyList<ReplicaSiloDiscovery>? discovery;
    private CancellationTokenSource? originalCancellation;
    private CancellationTokenSource? scenarioDeadline;
    private Task<NativeTextCapturedRf3Observation>? originalCall;
    private Guid arm;
    private Guid capturedArm;
    private Task<OnlineTextIndexMaintenanceResult>? capturedCall;
    private string publisherSecret = string.Empty;
    private string publisherId = string.Empty;
    private string publisherNode = string.Empty;

    internal static async Task RunAsync(NativeTextMaintenancePath path, bool cancelOriginal, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new NativeTextCapturedRf3Scenario(path, cancelOriginal);
        await ServerFailureObserver.ObserveAsync(() => scenario.ExecuteAsync(parent.Token), scenario.failures);
        await RequestCqrsPhaseFaultCleanup.RunAsync(scenario.root, scenario.rootOwned, scenario.controls,
            scenario.wave, scenario.startupAttempted, scenario.publisher, scenario.administrator, scenario.discovery,
            scenario.originalCancellation, scenario.scenarioDeadline, scenario.JoinOriginalCalls(), null, scenario.arm, scenario.failures, [scenario.capturedArm]);
        ServerFailureObserver.ThrowIfAny(scenario.failures);
    }

    private Task? JoinOriginalCalls()
        => originalCall is null ? capturedCall : capturedCall is null ? originalCall : Task.WhenAll(originalCall, capturedCall);

    private async Task ExecuteAsync(CancellationToken token)
    {
        root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        scenarioDeadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var current = scenarioDeadline.Token;
        var dataRoot = Path.Combine(root, "data");
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, current)).Profile;
        var images = await RequestCqrsRf3ImageProof.ReadAsync(current);
        controls = RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid());
        startupAttempted = true;
        wave = await RequestCqrsRf3Wave.StartProbedAsync(dataRoot,
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), controls, current);
        discovery = await AnnPublicCancellationAssertions.DiscoveryAsync(wave.App, profile, current);
        administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1, profile.AdminKey, current);
        var seed = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        await seed.SeedAsync(administrator.Sdk, current);
        var identity = await NativeTextCapturedRf3Publisher.CreateAsync(administrator.Sdk, seed.Partition, current);
        publisherSecret = identity.Secret;
        publisherId = identity.Principal.Id;
        var leader = await KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(current));
        var owner = discovery.Select((value, index) => (value, index)).Single(value => value.value.VoterId == leader.Leader);
        publisherNode = RequestCqrsRf3Protocol.NodeName(owner.index);
        publisher = await RequestCqrsRf3Callers.ConnectAsync(wave.App, publisherNode, publisherSecret, current);
        var source = await seed.RequestAsync(publisher.Sdk, publisher.Mcp, current);
        var request = new OnlineTextIndexMaintenanceRequest(source.CommandId, source.Consumer, source.Collection,
            source.Field, source.IndexGeneration, source.NodeId, source.Placement);
        _ = await KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await publisher.Sdk.ConfigureProjectionAsync(
            new(Guid.NewGuid(), request.Consumer, new(request.ConsumerGeneration, [request.Collection],
                ["putDocument", "patchDocument", "deleteDocument"])), current));
        var original = await NativeTextOnlineRf3Call.ExecuteAsync(publisher.Sdk, publisher.Mcp, request, path, current);
        await NativeTextOnlineRf3Assertions.ResultAsync(original, request, false);
        await NativeTextOnlineRf3Assertions.LiteralAllAsync(publisher.Sdk, publisher.Mcp, seed, false, current);
        await OverlapAsync(seed, request, original, current);
    }
}
