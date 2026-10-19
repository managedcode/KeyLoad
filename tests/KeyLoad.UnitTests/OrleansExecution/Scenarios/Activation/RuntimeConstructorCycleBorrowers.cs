#pragma warning disable ORLEANSEXP005
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.OrleansHosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.DurableJobs;
using Orleans.Journaling;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class RuntimeConstructorCycleBorrowers
{
    internal static async Task RunAsync(IServiceProvider services, string directory, CancellationToken token)
    {
        var hosted = services.GetServices<IHostedService>().ToArray();
        await Assert.That(hosted.Length).IsGreaterThan(0);
        await Assert.That(hosted.OfType<OrleansNodeLifecycle>().Single())
            .IsSameReferenceAs(services.GetRequiredService<OrleansNodeLifecycle>());
        _ = services.GetRequiredService<ILocalDurableJobManager>();
        var provider = services.GetRequiredKeyedService<IJournalStorageProvider>(RuntimeJournalRegistration.ProviderName);
        var catalog = services.GetRequiredKeyedService<IJournalStorageCatalog>(RuntimeJournalRegistration.ProviderName);
        await Assert.That(ReferenceEquals(provider, catalog)).IsTrue();
        var admission = services.GetRequiredService<RuntimeJournalAdmission>();
        var publication = services.GetRequiredService<Func<IPartitionStoragePublication>>()();
        await Assert.That(admission.IsReady).IsFalse();
        await Assert.That(Directory.Exists(directory)).IsFalse();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var enumerator = catalog.ListAsync(cancellationToken: cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        Task<bool>? originalCatalog = null;
        Task? originalPublication = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            originalPublication = publication.WaitForOpenAsync(cancellation.Token);
            originalCatalog = enumerator.MoveNextAsync().AsTask();
            await Assert.That(originalPublication.IsCompleted).IsFalse();
            await Assert.That(originalCatalog.IsCompleted).IsFalse();
            await Assert.That(admission.IsReady).IsFalse();
            await Assert.That(Directory.Exists(directory)).IsFalse();
        }, failures);
        await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures);
        await RuntimeConstructorCycleSettlement.JoinCancelledAsync(originalCatalog, failures, cancellation.Token);
        await RuntimeConstructorCycleSettlement.JoinCancelledAsync(originalPublication, failures, cancellation.Token);
        await ServerFailureObserver.ObserveAsync(() => enumerator.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(admission.IsReady).IsFalse();
            await Assert.That(Directory.Exists(directory)).IsFalse();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
#pragma warning restore ORLEANSEXP005
