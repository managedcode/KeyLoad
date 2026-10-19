using System.Net;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PeriodicMaintenanceFlow
{
    private const string RootPrefix = "keyload-execution-periodic-";
    private const string GuidFormat = "N";
    private const string InjectedFailure = "The original execution maintenance sweep failed.";
    private const string RetainedRoot = "KeyLoad.PeriodicMaintenance.RetainedRoot";
    private const int OneSilo = 1;
    private const int SweepIntervalMilliseconds = 20;
    private const int ColdCount = 2;
    private const int FirstCold = 0;

    internal static async Task RunAsync(bool failSweep, CancellationToken testToken)
    {
        var root = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(Path.GetTempPath())),
            RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var injected = failSweep ? new InvalidOperationException(InjectedFailure) : null;
        using var periodic = new PeriodicMaintenanceBarrier(injected);
        using var ports = new TestClusterPortAllocator();
        var failures = new List<Exception>();
        WebApplication? app = null;
        PartitionHostApplicationOwner? owner = null;
        var settled = false;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var port = ports.AllocateConsecutivePortPairs(OneSilo).Item1;
                var profile = new RequestCqrsHostedProfile(TimeProvider.System, UnitRoutingOptions.Routing(),
                    null, null, false, false, null, configureServices: Configure);
                var arguments = profile.Arguments(root, port);
                var localhost = new LocalhostClusteringOptions(new IPEndPoint(IPAddress.Loopback, port));
                app = await ServerConfiguration.BuildAsync(arguments, localhostClustering: localhost,
                    configureServices: profile.Register, startupCancellation: testToken);
                await app.StartAsync(testToken);
                owner = app.Services.GetRequiredService<PartitionHostApplicationOwner>();
                var original = await PartitionStorageHealthy.ExecuteAsync(app.Services, testToken);
                var store = (ZoneTreeStore)owner.Borrow().Database.Store;
                var identity = store.Identity;
                await ObserveSweepAsync(owner.Session.OwnerContext, store, periodic,
                    profile.Timing.CompletionTimeout, testToken);
                var stopping = app.StopAsync(testToken);
                await Assert.That(stopping.IsCompleted).IsFalse();
                periodic.Release();
                await SettleAsync(app, owner, stopping, injected);
                var released = new List<Exception>();
                PartitionStorageApplicationDisposeAssertions.RequireReleased(root, released);
                ServerFailureObserver.ThrowIfAny(released);
                settled = true;
                for (var cold = FirstCold; cold < ColdCount; cold++)
                {
                    _ = await PartitionStorageFlow.RunOwnerAsync(arguments, localhost, profile,
                        identity, original, testToken);
                }
            }, failures);
        }
        finally
        {
            periodic.Release();
            if (!settled && app is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures);
            }
            if (failures.Count == 0 && owner?.CanReleaseRoot == true)
            {
                ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
            }
            foreach (var failure in failures)
            {
                failure.Data[RetainedRoot] = root;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ObserveSweepAsync(PartitionStorageOwnerContext context, ZoneTreeStore store,
        PeriodicMaintenanceBarrier periodic, TimeSpan timeout, CancellationToken token)
    {
        await context.RunAsync(() =>
        {
            periodic.OwnerContext = context.Context;
            store.Maintenance.Swept = periodic.Sweep;
            return Task.CompletedTask;
        });
        var actual = await periodic.Swept.Task.WaitAsync(timeout, token);
        await Assert.That(ReferenceEquals(periodic.Context, context.Context)).IsTrue();
        await Assert.That(actual.ReadBuffers).IsGreaterThanOrEqualTo(0);
        await Assert.That(actual.CacheKeys).IsGreaterThanOrEqualTo(0);
        await Assert.That(actual.CacheValues).IsGreaterThanOrEqualTo(0);
    }

    private static void Configure(IServiceCollection services, NodeOptions node)
        => services.Replace(ServiceDescriptor.Singleton(UnitExecutionOptions.StorageExecution(new()
        {
            MaintenanceInterval = TimeSpan.FromMilliseconds(SweepIntervalMilliseconds),
            BlockCacheLifetime = TimeSpan.Zero
        })));

    private static async Task SettleAsync(WebApplication app, PartitionHostApplicationOwner owner,
        Task stopping, Exception? injected)
    {
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => stopping, observed);
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), observed);
        await Assert.That(owner.Closing is { IsCompleted: true }).IsTrue();
        if (injected is null)
        {
            ServerFailureObserver.ThrowIfAny(observed);
            await Assert.That(owner.CanReleaseRoot).IsTrue();
        }
        else
        {
            await Assert.That(observed.Any(failure => ContainsOriginal(failure, injected))).IsTrue();
            foreach (var failure in observed)
            {
                await Assert.That(OnlyExpected(failure, injected)).IsTrue();
            }
            await Assert.That(owner.CanReleaseRoot).IsFalse();
        }
    }

    private static bool ContainsOriginal(Exception failure, Exception original)
        => ReferenceEquals(failure, original) || failure is AggregateException aggregate
            && aggregate.InnerExceptions.Any(inner => ContainsOriginal(inner, original))
            || failure.InnerException is { } inner && ContainsOriginal(inner, original);

    private static bool OnlyExpected(Exception failure, Exception original)
    {
        if (ReferenceEquals(failure, original))
        {
            return true;
        }
        if (failure is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(inner => OnlyExpected(inner, original));
        }
        return failure.InnerException is { } inner && OnlyExpected(inner, original);
    }
}
