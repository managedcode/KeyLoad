using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
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
        var root = CreatePrivateRoot();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RunWavesAsync(root, deadline.Token), failures)
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

    private static async Task RunWavesAsync(string root, CancellationToken cancellationToken)
    {
        var images = await RequestCqrsRf3ImageProof.ReadAsync(cancellationToken).ConfigureAwait(false);
        var allCurrent = AllCurrent(images.Current);
        NodeEpochRf3Profile profile;
        RequestCqrsRf3Workload workload;
        await using (var original = await RequestCqrsRf3Wave.StartAsync(root, allCurrent,
            configureCohort: false, requireHealthy: true, cancellationToken).ConfigureAwait(false))
        {
            profile = (await NodeEpochRf3Profile.ReadAsync(Path.Combine(root,
                ClusterFixtureProtocol.ProfileFileName), cancellationToken).ConfigureAwait(false)).Profile;
            workload = await RequestCqrsRf3Workload.SeedAsync(original.App, profile, cancellationToken)
                .ConfigureAwait(false);
        }

        var conflictingIdentity = DifferentIdentity(profile.PhysicalShardId);
        EntityRef deniedReference;
        await using (var mismatch = await RequestCqrsRf3Wave.StartAsync(root, allCurrent,
            configureCohort: false, requireHealthy: false, Guid.NewGuid(), cancellationToken,
            physicalShardOverrideNode: RequestCqrsRf3Protocol.Node3,
            physicalShardOverrideId: conflictingIdentity).ConfigureAwait(false))
        {
            deniedReference = await PhysicalShardCatalogRf3MismatchAssertions.VerifyAsync(mismatch.App, profile,
                workload, RequestCqrsRf3Protocol.Node3, cancellationToken).ConfigureAwait(false);
        }
        await using var corrected = await RequestCqrsRf3Wave.StartAsync(root, allCurrent,
            configureCohort: false, requireHealthy: true, cancellationToken).ConfigureAwait(false);
        await workload.VerifyPreservedAsync(corrected.App, profile, cancellationToken).ConfigureAwait(false);
        await PhysicalShardCatalogRf3Assertions.VerifyAllVotersAsync(corrected.App, profile.AdminKey,
            Reference(workload), McpDocumentProtocol.InitialJson, cancellationToken).ConfigureAwait(false);
        await PhysicalShardCatalogRf3Assertions.VerifyAbsentAsync(corrected.App, profile.AdminKey,
            deniedReference, cancellationToken).ConfigureAwait(false);
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
