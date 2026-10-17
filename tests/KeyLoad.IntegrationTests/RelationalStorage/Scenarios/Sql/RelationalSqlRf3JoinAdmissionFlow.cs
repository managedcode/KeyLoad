using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed class RelationalSqlRf3JoinAdmissionFlow(bool officialMcp, bool ast)
{
    private const string MissingOwner = "The original Q2 admission owner was not retained.";
    private string root = string.Empty;
    private bool rootOwned;
    private bool waveStartupAttempted;
    private ClusterRouting.RequestCqrsProbeFixture? controls;
    private ClusterRouting.RequestCqrsRf3Wave? wave;
    private ClusterRouting.RequestCqrsRf3Callers? administrator;
    private ClusterRouting.RequestCqrsRf3Callers? caller;
    private IReadOnlyList<global::KeyLoad.Clustering.ReplicaSiloDiscovery>? discovery;
    private CancellationTokenSource? callerCancellation;
    private Task<RelationalQ2CancellationOutcome>? original;
    private Guid armId;
    private readonly List<Exception> failures = [];

    internal static async Task RunAsync(bool officialMcp, bool ast, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(ClusterRouting.RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var flow = new RelationalSqlRf3JoinAdmissionFlow(officialMcp, ast);
        await ClusterRouting.Helpers.RequestCqrsLifecycleFailureObserver.ObserveAsync(
            () => flow.ExecuteAsync(parent.Token), flow.failures, null,
            ClusterRouting.RequestCqrsLifecycleStage.FaultProfile);
        await ClusterRouting.RequestCqrsPhaseFaultCleanup.RunAsync(flow.root, flow.rootOwned,
            flow.controls, flow.wave, flow.waveStartupAttempted, flow.caller, flow.administrator,
            flow.discovery, flow.callerCancellation, null, flow.original, null, flow.armId, flow.failures);
        if (flow.failures.Count == 1)
        {
            global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(flow.failures[0]).Throw();
        }
        if (flow.failures.Count > 1)
        {
            throw new AggregateException(flow.failures);
        }
    }

    private async Task ExecuteAsync(CancellationToken parent)
    {
        using var startupTimeout = new CancellationTokenSource(ClusterRouting.RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(parent, startupTimeout.Token);
        var seed = await PrepareAsync(startup.Token);
        using var deadline = McpCallerDeadline.Create();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(parent, deadline.Token);
        var admin = administrator ?? throw new InvalidOperationException(MissingOwner);
        var activeCaller = caller ?? throw new InvalidOperationException(MissingOwner);
        var before = await RelationalSqlRf3JoinAdmissionAssertions.CaptureAsync(admin.Sdk, seed, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.HealthyAsync(activeCaller, seed, bounded.Token);
        await HoldCancelAndJoinAsync(seed, before, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(admin.Sdk, seed, before, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.HealthyAsync(activeCaller, seed, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(admin.Sdk, seed, before, bounded.Token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(seed.Command, bounded.Token));
        await SqlRf3Protocol.EqualAsync(seed.Receipt, replay);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(admin.Sdk, seed, before, bounded.Token);
    }

    private async Task<RelationalQ2AdmissionSeed> PrepareAsync(CancellationToken token)
    {
        root = ClusterRouting.RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        ClusterRouting.RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        var dataRoot = Path.Combine(root, "data");
        var profile = (await global::KeyLoad.IntegrationTests.Features.StorageRecovery.NodeEpochRf3Profile.CreatePriorAsync(dataRoot, token)).Profile;
        var image = await ClusterRouting.RequestCqrsRf3ImageProof.ReadAsync(token);
        controls = ClusterRouting.RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid());
        waveStartupAttempted = true;
        wave = await ClusterRouting.RequestCqrsRf3Wave.StartProbedAsync(dataRoot,
            ClusterRouting.RequestCqrsPhaseFaultProvisioning.CurrentImages(image.Current), controls, token);
        discovery = await ClusterRouting.RequestCqrsAuthorityFaultDiscovery.ReadAsync(wave.App, profile, token);
        administrator = await ClusterRouting.RequestCqrsRf3Callers.ConnectAsync(wave.App,
            ClusterRouting.RequestCqrsRf3Protocol.Node1, profile.AdminKey, token);
        var seed = await RelationalSqlRf3JoinAdmissionSeed.CreateAsync(administrator.Sdk, token);
        caller = await ClusterRouting.RequestCqrsRf3Callers.ConnectAsync(wave.App,
            ClusterRouting.RequestCqrsRf3Protocol.Node2, seed.Identity.Secret, token);
        return seed;
    }

    private async Task HoldCancelAndJoinAsync(RelationalQ2AdmissionSeed seed, string[] before, CancellationToken token)
    {
        var activeControls = controls ?? throw new InvalidOperationException(MissingOwner);
        var actualDiscovery = discovery ?? throw new InvalidOperationException(MissingOwner);
        var activeCaller = caller ?? throw new InvalidOperationException(MissingOwner);
        var kind = ast ? global::KeyLoad.Clustering.GrainReadKind.AstQuery : global::KeyLoad.Clustering.GrainReadKind.Query;
        armId = activeControls.WriteArm(seed.Identity.Principal.Id, Guid.Empty, kind,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeAction.Hold);
        callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await Assert.That(callerCancellation.IsCancellationRequested).IsFalse();
        original = RelationalSqlRf3JoinAdmissionOperation.StartAsync(activeCaller, seed,
            officialMcp, ast, callerCancellation.Token);
        var observed = await activeControls.WaitForMarkerAsync(armId,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Observed, actualDiscovery, token);
        await Assert.That(original.IsCompleted).IsFalse();
        await Assert.That(observed.RequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(observed.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(observed.ArmId).IsEqualTo(armId);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(
            (administrator ?? throw new InvalidOperationException(MissingOwner)).Sdk, seed, before, token);
        await Assert.That(original.IsCompleted).IsFalse();
        await callerCancellation.CancelAsync();
        var cancelled = await activeControls.WaitForMarkerAsync(armId,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Cancelled, actualDiscovery, token);
        await SqlRf3Protocol.EqualAsync(observed with
        {
            Outcome = global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Cancelled
        }, cancelled);
        var disposed = await activeControls.WaitForMarkerAsync(armId,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.ProducerDisposed,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Observed, actualDiscovery, token);
        await SqlRf3Protocol.EqualAsync(observed with
        {
            Phase = global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.ProducerDisposed
        }, disposed);
        await Assert.That(activeControls.ArmFor(armId).Settled).IsTrue();
        await Assert.That(activeControls.ArmFor(armId).ProducerDisposedSeen).IsTrue();
        var outcome = await original;
        await Assert.That(callerCancellation.IsCancellationRequested).IsTrue();
        await RelationalSqlRf3JoinAdmissionAssertions.CancelledAsync(outcome, officialMcp);
        await activeControls.RetireArmAsync(armId, token);
    }
}
