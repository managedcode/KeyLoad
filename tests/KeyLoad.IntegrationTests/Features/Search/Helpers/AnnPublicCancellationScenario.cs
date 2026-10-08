using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class AnnPublicCancellationScenario(bool useMcp)
{
    private const string MissingOwner = "The public ANN cancellation owner is unavailable.";
    private const string ReadUnavailable = "The read response is unavailable.";
    private const int Version = 1;
    private readonly List<Exception> failures = [];
    private string root = string.Empty;
    private bool rootOwned;
    private bool startupAttempted;
    private RequestCqrsProbeFixture? controls;
    private RequestCqrsRf3Wave? wave;
    private RequestCqrsRf3Callers? administrator;
    private RequestCqrsRf3Callers? caller;
    private IReadOnlyList<ReplicaSiloDiscovery>? discovery;
    private CancellationTokenSource? callerCancellation;
    private CancellationTokenSource? scenarioDeadline;
    private Task<Result<AnnSearchPage>>? sdkCall;
    private Task<RequestCqrsFaultMcpObservation>? mcpCall;
    private Guid armId;

    internal static async Task RunAsync(bool useMcp, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new AnnPublicCancellationScenario(useMcp);
        await ServerFailureObserver.ObserveAsync(() => scenario.ExecuteAsync(parent.Token), scenario.failures);
        await RequestCqrsPhaseFaultCleanup.RunAsync(scenario.root, scenario.rootOwned, scenario.controls,
            scenario.wave, scenario.startupAttempted, scenario.caller, scenario.administrator, scenario.discovery,
            scenario.callerCancellation, scenario.scenarioDeadline, scenario.sdkCall, scenario.mcpCall,
            scenario.armId, scenario.failures);
        ServerFailureObserver.ThrowIfAny(scenario.failures);
    }

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
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), controls, current,
            queryExecution: new QueryExecutionOptions { EnableApproximateSearch = true });
        discovery = await AnnPublicCancellationAssertions.DiscoveryAsync(wave.App, profile, current);
        administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1, profile.AdminKey, current);
        var seed = new NativeAnnMaintenanceRf3Scenario();
        await seed.SeedAsync(administrator.Sdk, current);
        var pin = await seed.RequestAsync(administrator.Sdk, administrator.Mcp, current);
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.MaintainAnnIndexAsync(pin, current)), pin);
        var identity = await AnnPublicCancellationAssertions.IdentityAsync(administrator.Sdk, seed.Partition, current);
        caller = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1, identity.Secret, current);
        await CancelAndContinueAsync(seed, pin, identity.Principal.Id, current);
    }

    private async Task CancelAndContinueAsync(NativeAnnMaintenanceRf3Scenario seed,
        AnnMaintenanceRequest pin, string principalId, CancellationToken token)
    {
        var activeControls = controls ?? throw new InvalidOperationException(MissingOwner);
        var actualDiscovery = discovery ?? throw new InvalidOperationException(MissingOwner);
        var activeCaller = caller ?? throw new InvalidOperationException(MissingOwner);
        var admin = administrator ?? throw new InvalidOperationException(MissingOwner);
        var request = new ApproximateSearchRequest(Version, seed.Search(), pin.Consumer, pin.IndexGeneration);
        var before = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
        armId = activeControls.WriteArm(principalId, Guid.Empty, GrainReadKind.ApproximateSearch,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (useMcp)
        { mcpCall = AnnPublicCancellationAssertions.CallMcpAsync(activeCaller.Mcp, request, callerCancellation.Token); }
        else
        { sdkCall = activeCaller.Sdk.ApproximateSearchAsync(request, callerCancellation.Token); }
        var marker = await activeControls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, actualDiscovery, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, actualDiscovery);
        await callerCancellation.CancelAsync();
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(activeControls, armId, marker.RequestId,
            Guid.Empty, actualDiscovery, token);
        await AssertOriginalAsync();
        await activeControls.RetireArmAsync(armId, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(admin.Sdk, admin.Mcp, seed, token);
        await AnnPublicRf3Assertions.AllPathsAsync(activeCaller.Sdk, activeCaller.Mcp, seed, pin, false, token);
    }

    private async Task AssertOriginalAsync()
    {
        await Assert.That(callerCancellation?.IsCancellationRequested).IsTrue();
        if (useMcp)
        {
            var observed = await (mcpCall ?? throw new InvalidOperationException(MissingOwner));
            await Assert.That(observed.ToolResult).IsNull();
            await Assert.That(observed.TransportFailure is OperationCanceledException or HttpRequestException or IOException).IsTrue();
            return;
        }
        var result = await (sdkCall ?? throw new InvalidOperationException(MissingOwner));
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.Cancelled.ToString());
        await Assert.That(result.Problem?.Detail).IsEqualTo(ReadUnavailable);
    }
}
