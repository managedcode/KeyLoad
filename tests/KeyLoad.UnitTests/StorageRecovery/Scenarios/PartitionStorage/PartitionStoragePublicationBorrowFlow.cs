using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStoragePublicationBorrowFlow
{
    private const string NativeKey = "publication-borrow-native-control";
    private const string NativeValue = "original durable canonical bytes";
    private const int ColdOwners = 2;
    private const int FirstColdOwner = 0;
    private const int NoFailures = 0;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        string? directory = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new PartitionStoragePublicationBorrowFixture();
            directory = fixture.DirectoryPath;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.OpenAsync().ConfigureAwait(false);
                await BorrowThroughNativeContainerAsync(fixture, token).ConfigureAwait(false);
                var host = fixture.Owner.Borrow();
                var before = host.Database.Store.Position;
                var committed = host.Database.Store.Commit((transaction, position) =>
                {
                    transaction.Put(Encoding.UTF8.GetBytes(NativeKey), Encoding.UTF8.GetBytes(NativeValue));
                    return position;
                });
                await Assert.That(committed).IsEqualTo(checked(before + 1));
                var image = host.Database.Store.Read(view => PartitionStorageNativeImage.Capture(host, view, token));
                await RequireValueAsync(host.Database.Store).ConfigureAwait(false);
                for (var cold = FirstColdOwner; cold < ColdOwners; cold++)
                {
                    await fixture.OpenAsync().ConfigureAwait(false);
                    host = fixture.Owner.Borrow();
                    var actual = host.Database.Store.Read(view => PartitionStorageNativeImage.Capture(host, view, token));
                    await PartitionStorageNativeImage.RequireAsync(image, actual).ConfigureAwait(false);
                    await RequireValueAsync(host.Database.Store).ConfigureAwait(false);
                }
            }, failures).ConfigureAwait(false);
            if (failures.Count != NoFailures)
            {
                fixture.RetainEvidence();
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(directory is not null).IsTrue();
        await Assert.That(Directory.Exists(directory!)).IsFalse();
    }

    private static async Task BorrowThroughNativeContainerAsync(PartitionStoragePublicationBorrowFixture fixture,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            IHost? container = null;
            await using var closing = (IAsyncDisposable)(container = new HostBuilder()
                .ConfigureServices(services => services.AddSingleton(fixture.Owner))
                .UseOrleans(silo => PartitionStorageRegistration.Register(silo))
                .Build());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                // Resolve any accidental direct alias so actual DI disposal would also capture it.
                var direct = container.Services.GetServices<IPartitionStoragePublication>().ToArray();
                var publication = container.Services.GetRequiredService<Func<IPartitionStoragePublication>>()();
                await Assert.That(ReferenceEquals(publication, fixture.Owner)).IsTrue();
                await Assert.That(ReferenceEquals(publication.Opening, fixture.Owner.Opening)).IsTrue();
                await publication.WaitForOpenAsync(token).ConfigureAwait(false);
                await Assert.That(direct).IsEmpty();
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(fixture.Owner.Closing is null).IsTrue();
        await fixture.Owner.WaitForOpenAsync(token).ConfigureAwait(false);
    }

    private static async Task RequireValueAsync(IAtomicStore store)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(Encoding.UTF8.GetBytes(NativeKey)));
        await Assert.That(bytes is not null).IsTrue();
        await Assert.That(bytes!.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(NativeValue))).IsTrue();
    }
}
