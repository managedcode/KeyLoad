using System.Net;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativePreStartDisposeFlow
{
    private const string RootPrefix = "keyload-native-prestart-";
    private const string DirectoryName = "node";
    private const string GuidFormat = "N";
    private const string MissingHealthyOwner = "The healthy native application did not publish its storage identity and receipt.";
    private const int OneSilo = 1;
    private const int FirstCold = 0;
    private const int ColdCount = 2;
    private const int NoFailures = 0;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        var root = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(Path.GetTempPath())),
            RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var directory = Path.Combine(root, DirectoryName);
        var ports = new TestClusterPortAllocator();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Directory.CreateDirectory(root);
            var port = ports.AllocateConsecutivePortPairs(OneSilo).Item1;
            var profile = new RequestCqrsHostedProfile(TimeProvider.System, UnitRoutingOptions.Routing(),
                null, null, false, false, null);
            var arguments = profile.Arguments(directory, port);
            var localhost = new NativeLocalhostClusteringOptions(new IPEndPoint(IPAddress.Loopback, port));
            await PartitionStorageNativePreStartDisposeTrial.RunAsync(arguments, localhost, profile, root, directory, token)
                .ConfigureAwait(false);
            var original = await RunOwnerAsync(arguments, localhost, profile, directory, null, token)
                .ConfigureAwait(false);
            for (var cold = FirstCold; cold < ColdCount; cold++)
            {
                _ = await RunOwnerAsync(arguments, localhost, profile, directory, original, token)
                    .ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(ports.Dispose, failures);
        }
        if (failures.Count == NoFailures && Directory.Exists(root))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<(StoreIdentity Identity, PartitionStorageNativeCommit Receipt)> RunOwnerAsync(
        string[] arguments, NativeLocalhostClusteringOptions localhost, RequestCqrsHostedProfile profile,
        string directory, (StoreIdentity Identity, PartitionStorageNativeCommit Receipt)? previous,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        WebApplication? application = null;
        PartitionHostApplicationOwner? owner = null;
        OrleansNode? node = null;
        StoreIdentity? identity = null;
        PartitionStorageNativeCommit? receipt = null;
        Task? disposal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(profile.Timing.StartupTimeout, TimeProvider.System);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
            application = await ServerConfiguration.BuildAsync(arguments, localhostClustering: localhost,
                configureServices: profile.Register, startupCancellation: startup.Token).ConfigureAwait(false);
            owner = application.Services.GetRequiredService<PartitionHostApplicationOwner>();
            node = application.Services.GetRequiredService<OrleansNode>();
            await PartitionStorageNativeStartupWaitAssertions.StartAsync(application, node, startup.Token)
                .ConfigureAwait(false);
            identity = owner.Borrow().Database.Store.Identity;
            if (previous is { } retained)
            {
                await Assert.That(JsonDefaults.Serialize(identity).AsSpan()
                    .SequenceEqual(JsonDefaults.Serialize(retained.Identity))).IsTrue();
                receipt = retained.Receipt;
            }
            else
            {
                receipt = await PartitionStorageNativeHealthy.ExecuteAsync(application.Services, token).ConfigureAwait(false);
            }
            await PartitionStorageNativeHealthy.ReplayAsync(application.Services, receipt, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (application is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => disposal = application.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (owner is not null && node is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(disposal is { IsCompletedSuccessfully: true }).IsTrue();
                await Assert.That(owner.Closing is { IsCompletedSuccessfully: true }).IsTrue();
                await Assert.That(node.Closing is { IsCompletedSuccessfully: true }).IsTrue();
                await Assert.That(owner.CanReleaseRoot).IsTrue();
                await Assert.That(node.HasJoinedRequestWork).IsTrue();
            }, failures).ConfigureAwait(false);
            if (owner.CanReleaseRoot)
            {
                PartitionStorageNativeApplicationDisposeAssertions.RequireReleased(directory, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return (identity ?? throw new InvalidOperationException(MissingHealthyOwner),
            receipt ?? throw new InvalidOperationException(MissingHealthyOwner));
    }
}
