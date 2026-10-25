using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PartitionRootAdmissionFixture : IDisposable
{
    private static readonly byte[] Key = KeyCodec.Encode("current-layout", "healthy-followup");
    private static readonly byte[] Value = "current-layout-write"u8.ToArray();
    private readonly PartitionHostRecoveryFixture hostFixture = new();

    internal string Root => hostFixture.Options.DataDirectory;

    internal void CreateExistingRoot()
    {
        Directory.CreateDirectory(Root);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    internal Task OpenAndDisposeHostAsync() => hostFixture.OpenAndDisposeHostAsync();

    internal async Task AssertHealthyWriteReopenAsync()
    {
        await hostFixture.RunHostAsync(async host =>
        {
            host.Database.Store.Commit((transaction, _) =>
            {
                transaction.Put(Key, Value);
                return true;
            });
            await Assert.That(host.Database.Store.Read(view => view.ReadOwnedValue(Key))).IsEquivalentTo(Value);
        }).ConfigureAwait(false);
        await hostFixture.RunHostAsync(async reopened =>
        {
            await Assert.That(reopened.Database.Store.Read(view => view.ReadOwnedValue(Key))).IsEquivalentTo(Value);
        }).ConfigureAwait(false);
    }

    internal static async Task RunAsync(Func<PartitionRootAdmissionFixture, Task> body)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PartitionRootAdmissionFixture();
            await ServerFailureObserver.ObserveAsync(() => body(fixture), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose() => hostFixture.Dispose();
}
