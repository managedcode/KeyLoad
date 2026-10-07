using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalShardCatalogRf3MismatchTests
{
    private const string RetainedRootKey = "KeyLoad.PhysicalShardCatalog.Rf3MismatchRoot";
    private const string DocumentZero = "document-00";

    [Test]
    public async Task AcScat003OneVoterWithConflictingShardIdentityStaysFencedAndDeniesAdmission()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        var lifecycle = new RequestCqrsLifecycleEvidence();
        lifecycle.SetTokens(TestContext.Current!.Execution.CancellationToken, deadline.Token, default);
        var root = CreatePrivateRoot();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RunWavesAsync(root, lifecycle, deadline.Token), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            { failure.Data[RetainedRootKey] = root; }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task RunWavesAsync(string root, RequestCqrsLifecycleEvidence lifecycle,
        CancellationToken cancellationToken)
    {
        var images = await RequestCqrsRf3ImageProof.ReadAsync(cancellationToken).ConfigureAwait(false);
        var allCurrent = AllCurrent(images.Current);
        var (profile, workload) = await OriginalWaveAsync(root, allCurrent, lifecycle, cancellationToken)
            .ConfigureAwait(false);
        var deniedReference = await MismatchWaveAsync(root, allCurrent, lifecycle, profile, workload,
            cancellationToken).ConfigureAwait(false);
        lifecycle.SetScenarioPhase(RequestCqrsLifecycleStage.ScatCorrectedWave);
        await RequestCqrsRf3Epoch7WaveRunner.RunObservedAsync(root, allCurrent, false, true, async corrected =>
        {
            lifecycle.SetStage(RequestCqrsLifecycleStage.ScatCorrectedAssertions);
            await workload.VerifyPreservedAsync(corrected.App, profile, cancellationToken).ConfigureAwait(false);
            await PhysicalShardCatalogRf3Assertions.VerifyAllVotersAsync(corrected.App, profile.AdminKey,
                Reference(workload), workload.InitialDocuments[0], cancellationToken).ConfigureAwait(false);
            await PhysicalShardCatalogRf3Assertions.VerifyAbsentAsync(corrected.App, profile.AdminKey,
                deniedReference, cancellationToken).ConfigureAwait(false);
        }, new(lifecycle), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(NodeEpochRf3Profile Profile, RequestCqrsRf3Workload Workload)> OriginalWaveAsync(
        string root, IReadOnlyDictionary<string, string> images, RequestCqrsLifecycleEvidence lifecycle,
        CancellationToken cancellationToken)
    {
        NodeEpochRf3Profile profile = null!;
        RequestCqrsRf3Workload workload = null!;
        lifecycle.SetScenarioPhase(RequestCqrsLifecycleStage.ScatOriginalWave);
        await RequestCqrsRf3Epoch7WaveRunner.RunObservedAsync(root, images, false, true, async original =>
        {
            lifecycle.SetStage(RequestCqrsLifecycleStage.ScatSeed);
            profile = (await NodeEpochRf3Profile.ReadAsync(Path.Combine(root,
                ClusterFixtureProtocol.ProfileFileName), cancellationToken).ConfigureAwait(false)).Profile;
            workload = await RequestCqrsRf3Workload.SeedAsync(original.App, profile, cancellationToken)
                .ConfigureAwait(false);
        }, new(lifecycle), cancellationToken).ConfigureAwait(false);
        return (profile, workload);
    }

    private static async Task<EntityRef> MismatchWaveAsync(string root, IReadOnlyDictionary<string, string> images,
        RequestCqrsLifecycleEvidence lifecycle, NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload,
        CancellationToken cancellationToken)
    {
        EntityRef denied = null!;
        var conflictingIdentity = DifferentIdentity(profile.PhysicalShardId);
        lifecycle.SetScenarioPhase(RequestCqrsLifecycleStage.ScatMismatchWave);
        await RequestCqrsRf3Epoch7WaveRunner.RunObservedAsync(root, images, false, false, async mismatch =>
        {
            lifecycle.SetStage(RequestCqrsLifecycleStage.ScatMismatchAssertions);
            denied = await PhysicalShardCatalogRf3MismatchAssertions.VerifyAsync(mismatch.App, profile,
                workload, RequestCqrsRf3Protocol.Node3, cancellationToken).ConfigureAwait(false);
        }, new(lifecycle, RequestCqrsRf3Protocol.Node3, conflictingIdentity), cancellationToken).ConfigureAwait(false);
        return denied;
    }

    private static Dictionary<string, string> AllCurrent(string image)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = image,
            [RequestCqrsRf3Protocol.Node2] = image,
            [RequestCqrsRf3Protocol.Node3] = image
        };

    private static Guid DifferentIdentity(Guid configured)
    {
        Guid candidate;
        do
        { candidate = Guid.NewGuid(); }
        while (candidate == Guid.Empty || candidate == configured);
        return candidate;
    }

    private static EntityRef Reference(RequestCqrsRf3Workload workload)
        => new(workload.Partition, RequestCqrsRf3Protocol.AdminCollection, DocumentZero);

    private static string CreatePrivateRoot()
    {
        var root = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification", "physical-shard-catalog-rf3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return root;
    }
}
