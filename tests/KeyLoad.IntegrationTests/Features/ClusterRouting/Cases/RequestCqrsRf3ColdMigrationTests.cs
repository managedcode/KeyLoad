using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsRf3ColdMigrationTests
{
    [Test]
    public async Task SameEpochProtocolMismatchIsRejectedAndSupportedDataRollsBetweenBinaries()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(RequestCqrsRf3Protocol.ParentDeadline);
        var dataRoot = CreatePrivateRoot();
        var (profile, profileBytes) = await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, deadline.Token)
            .ConfigureAwait(false);
        var images = await RequestCqrsRf3ImageProof.ReadAsync(deadline.Token).ConfigureAwait(false);
        var workload = new RequestCqrsRf3WorkloadHolder();
        var currentWrite = new RequestCqrsRf3ReceiptHolder();
        await RunWaveAsync(dataRoot, All(images.Rpc1), true, true, async wave =>
        {
            workload.Value = await RequestCqrsRf3Workload.SeedAsync(wave.App, profile, deadline.Token)
                .ConfigureAwait(false);
            await VerifyProfileAsync(dataRoot, profileBytes, profile, deadline.Token).ConfigureAwait(false);
        }, deadline.Token).ConfigureAwait(false);
        await RunWaveAsync(dataRoot, Mixed(images), true, false,
            wave => RequestCqrsRf3MixedOracle.VerifyAsync(wave.App, profile, workload.Value!, deadline.Token),
            deadline.Token).ConfigureAwait(false);
        await RunWaveAsync(dataRoot, All(images.Current), false, true, async wave =>
        {
            await VerifyProfileAsync(dataRoot, profileBytes, profile, deadline.Token).ConfigureAwait(false);
            await workload.Value!.VerifyPreservedAsync(wave.App, profile, deadline.Token).ConfigureAwait(false);
            currentWrite.Value = await workload.Value.AppendCurrentWriteAsync(wave.App, profile,
                RequestCqrsRf3Protocol.Node1, deadline.Token)
                .ConfigureAwait(false);
        }, deadline.Token).ConfigureAwait(false);
        await RunWaveAsync(dataRoot, All(images.Rpc1), true, true, async wave =>
        {
            await VerifyProfileAsync(dataRoot, profileBytes, profile, deadline.Token).ConfigureAwait(false);
            await workload.Value!.VerifyPreservedAsync(wave.App, profile, deadline.Token, currentWriteExists: true)
                .ConfigureAwait(false);
            await workload.Value.VerifyCurrentWriteAsync(wave.App, profile, RequestCqrsRf3Protocol.Node1,
                currentWrite.Value ?? throw new InvalidOperationException("The current write receipt is missing."),
                deadline.Token).ConfigureAwait(false);
        }, deadline.Token).ConfigureAwait(false);
        Directory.Delete(dataRoot, recursive: true);
    }

    private static async Task RunWaveAsync(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, bool requireHealthy, Func<RequestCqrsRf3Wave, Task> action,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        try
        {
            await using var wave = await RequestCqrsRf3Wave.StartAsync(dataRoot, images, configureCohort,
                requireHealthy, cancellationToken).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => action(wave), failures).ConfigureAwait(false);
        }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyProfileAsync(string dataRoot, byte[] expected, NodeEpochRf3Profile profile,
        CancellationToken cancellationToken)
    {
        var actual = await NodeEpochRf3Profile.ReadAsync(Path.Combine(dataRoot, NodeEpochRf3Protocol.ProfileFile),
            cancellationToken).ConfigureAwait(false);
        await Assert.That(actual.Bytes.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Profile.Incarnation).IsEqualTo(profile.Incarnation);
    }

    private static string CreatePrivateRoot()
    {
        var artifacts = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification");
        Directory.CreateDirectory(artifacts);
        var path = Path.Combine(artifacts, "cluster-routing-c1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return path;
    }

    private static Dictionary<string, string> All(string reference)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = reference,
            [RequestCqrsRf3Protocol.Node2] = reference,
            [RequestCqrsRf3Protocol.Node3] = reference
        };

    private static Dictionary<string, string> Mixed(RequestCqrsRf3Images images)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = images.Current,
            [RequestCqrsRf3Protocol.Node2] = images.Current,
            [RequestCqrsRf3Protocol.Node3] = images.Rpc1
        };

    private sealed class RequestCqrsRf3WorkloadHolder { internal RequestCqrsRf3Workload? Value { get; set; } }
    private sealed class RequestCqrsRf3ReceiptHolder { internal CommitReceipt? Value { get; set; } }
}
