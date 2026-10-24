using KeyLoad.RecoveryTests.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class PartitionStorageFlow
{
    private const string DirectoryPrefix = "keyload-execution-storage-";
    private const string GuidFormat = "N";
    private const string MissingReceipt = "The original execution storage operation did not return its receipt.";
    private const int OneSilo = 1;
    private const int FirstCold = 0;
    private const int ColdCount = 2;
    private const int NoFailures = 0;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        var directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
            DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        using var ports = new TestClusterPortAllocator();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var port = ports.AllocateConsecutivePortPairs(OneSilo).Item1;
            var profile = new RequestCqrsHostedProfile(TimeProvider.System, UnitRoutingOptions.Routing(),
                null, null, false, false, null);
            var arguments = profile.Arguments(directory, port);
            var partial = await PartitionStoragePartialTrial.RunAsync(arguments, profile, token)
                .ConfigureAwait(false);
            var released = new List<Exception>();
            PartitionStorageApplicationDisposeAssertions.RequireReleased(directory, released);
            ServerFailureObserver.ThrowIfAny(released);
            var original = await RunOwnerAsync(arguments, profile, partial.Identity!, null, token)
                .ConfigureAwait(false);
            for (var cold = FirstCold; cold < ColdCount; cold++)
            {
                _ = await RunOwnerAsync(arguments, profile, partial.Identity!, original, token)
                    .ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(ports.Dispose, failures);
        }
        if (failures.Count == NoFailures && Directory.Exists(directory))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(directory, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task<PartitionStorageCommit> RunOwnerAsync(string[] arguments,
        RequestCqrsHostedProfile profile, StoreIdentity identity,
        PartitionStorageCommit? original, CancellationToken token)
    {
        var failures = new List<Exception>();
        WebApplication? application = null;
        PartitionHostApplicationOwner? owner = null;
        OrleansNode? node = null;
        string? directory = null;
        Task? disposal = null;
        var receipt = original;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(profile.Timing.StartupTimeout, TimeProvider.System);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
            application = await ServerConfiguration.BuildAsync(arguments,
                configureServices: profile.Register, startupCancellation: startup.Token).ConfigureAwait(false);
            owner = application.Services.GetRequiredService<PartitionHostApplicationOwner>();
            node = application.Services.GetRequiredService<OrleansNode>();
            await application.StartAsync(startup.Token).ConfigureAwait(false);
            var host = owner.Borrow();
            directory = host.DirectoryPath;
            await Assert.That(JsonDefaults.Serialize(host.Database.Store.Identity).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(identity))).IsTrue();
            if (original is null)
            {
                receipt = await PartitionStorageHealthy.ExecuteAsync(application.Services, token).ConfigureAwait(false);
            }
            await PartitionStorageHealthy.ReplayAsync(application.Services,
                receipt ?? throw new InvalidOperationException(MissingReceipt), token).ConfigureAwait(false);
            await application.StopAsync(token);
            await Assert.That(node.HasJoinedRequestWork).IsTrue();
            await Assert.That(node.DatabaseReady).IsFalse();
            await Assert.That(owner.CanReleaseRoot).IsTrue();
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
                await Assert.That(owner.CanReleaseRoot).IsTrue();
                await Assert.That(node.HasJoinedRequestWork).IsTrue();
            }, failures).ConfigureAwait(false);
        }
        if (directory is not null && owner?.CanReleaseRoot == true)
        {
            PartitionStorageApplicationDisposeAssertions.RequireReleased(directory, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return receipt ?? throw new InvalidOperationException(MissingReceipt);
    }
}
