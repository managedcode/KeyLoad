using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionStorageNativePartialStart(PartitionHostApplicationOwner owner)
    : global::Orleans.ILifecycleParticipant<global::Orleans.Runtime.ISiloLifecycle>
{
    private const string Name = nameof(PartitionStorageNativePartialStart);
    private const string MissingOpening = "Native storage initialization did not publish its original operation.";

    internal Task? Callback
    {
        get; private set;
    }
    internal Task? Opening
    {
        get; private set;
    }
    internal Task? PublicationWait
    {
        get; private set;
    }
    internal StoreIdentity? Identity
    {
        get; private set;
    }
    internal long Position
    {
        get; private set;
    }
    internal IOException? Refusal
    {
        get; private set;
    }
    internal bool UnexpectedAcquisition
    {
        get; private set;
    }

    internal PartitionStorageNativeImage? Before
    {
        get; private set;
    }
    internal PartitionStorageNativeImage? After
    {
        get; private set;
    }

    public void Participate(global::Orleans.Runtime.ISiloLifecycle lifecycle)
        => global::Orleans.LifecycleExtensions.Subscribe(lifecycle, Name,
            global::Orleans.ServiceLifecycleStage.ApplicationServices, StartAsync);

    private Task StartAsync(CancellationToken token) => Callback ??= FailAfterPublicationAsync(token);

    private async Task FailAfterPublicationAsync(CancellationToken token)
    {
        Opening = owner.Opening ?? throw new InvalidOperationException(MissingOpening);
        PublicationWait = owner.WaitForOpenAsync(token);
        await PublicationWait.ConfigureAwait(false);
        var host = owner.Borrow();
        host.Database.Store.Read(view =>
        {
            Before = PartitionStorageNativeImage.Capture(host, view, token);
            Identity = Before.Identity;
            Position = Before.Position;
            token.ThrowIfCancellationRequested();
            FileStream unexpected;
            try
            {
                unexpected = new FileStream(Path.Combine(host.DirectoryPath, PartitionStoreProtocol.OwnershipFile),
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException original)
            {
                Refusal = original;
                var failures = new List<Exception> { original };
                ServerFailureObserver.Observe(() => After = PartitionStorageNativeImage.Capture(host, view, token),
                    failures);
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
            UnexpectedAcquisition = true;
            unexpected.Dispose();
            return true;
        });
    }
}
