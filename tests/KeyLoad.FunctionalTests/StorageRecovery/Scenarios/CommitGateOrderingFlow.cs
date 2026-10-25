using A = KeyLoad.FunctionalTests.StorageRecovery.PreparationOwnershipAssertions;
using O = KeyLoad.FunctionalTests.StorageRecovery.PreparationOwnershipOwner;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class CommitGateOrderingFlow
{
    private const int BoundSeconds = 5;
    private const int SettleMilliseconds = 50;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(BoundSeconds);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(SettleMilliseconds);
    private const string WriterNotCompiling = "The writer did not enter its compiler callback.";
    private const string WriterNotReleased = "The compiling writer was not released.";
    private const string ReaderBlocked = "A reader waited for a writer that was still compiling.";
    private const string ReadNotEntered = "The held read did not enter the store gate.";
    private const string ReadNotReleased = "The held read was not released.";

    internal static async Task RunAsync(O owner)
    {
        using var compiling = new ManualResetEventSlim();
        using var releaseWriter = new ManualResetEventSlim();
        using var reading = new ManualResetEventSlim();
        using var releaseReader = new ManualResetEventSlim();
        var writer = Start(() => owner.Store.Commit((transaction, _) =>
        {
            compiling.Set();
            if (!releaseWriter.Wait(Bound))
            {
                throw new TimeoutException(WriterNotReleased);
            }
            transaction.Put(O.ParentKey, O.ParentValue);
            return true;
        }));
        try
        {
            if (!compiling.Wait(Bound))
            {
                throw new TimeoutException(WriterNotCompiling);
            }
            var read = Start(() => owner.Store.Read(view => view.ReadOwnedValue(O.SeedKey)));
            byte[]? seed;
            try
            {
                seed = await read.WaitAsync(Bound);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(ReaderBlocked);
            }
            await A.BytesAsync(O.SeedValue, seed);
            await Assert.That(writer.IsCompleted).IsFalse();

            var held = Start(() => owner.Store.Read(view =>
            {
                reading.Set();
                return releaseReader.Wait(Bound) ? view.ReadOwnedValue(O.ParentKey) : throw new TimeoutException(ReadNotReleased);
            }));
            if (!reading.Wait(Bound))
            {
                throw new TimeoutException(ReadNotEntered);
            }
            releaseWriter.Set();
            await Task.Delay(Settle, TimeProvider.System);
            await Assert.That(writer.IsCompleted).IsFalse();
            releaseReader.Set();
            await Assert.That(await held).IsNull();
        }
        finally
        {
            releaseWriter.Set();
            releaseReader.Set();
        }
        await Assert.That(await writer).IsTrue();
        await A.BytesAsync(O.ParentValue, owner.Store.Read(view => view.ReadOwnedValue(O.ParentKey)));
        await A.HealthyAsync(owner);
    }

    private static Task<T> Start<T>(Func<T> operation)
        => Task.Factory.StartNew(operation, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
