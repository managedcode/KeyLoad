using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

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

    internal PartitionHost OpenHost() => hostFixture.OpenHost();

    internal Task OpenAndDisposeHostAsync() => hostFixture.OpenAndDisposeHostAsync();

    internal async Task AssertHealthyWriteReopenAsync()
    {
        await using (var host = OpenHost())
        {
            host.Database.Store.Commit((transaction, _) =>
            {
                transaction.Put(Key, Value);
                return true;
            });
            await Assert.That(host.Database.Store.Read(view => view.ReadOwnedValue(Key))).IsEquivalentTo(Value);
        }
        await using var reopened = OpenHost();
        await Assert.That(reopened.Database.Store.Read(view => view.ReadOwnedValue(Key))).IsEquivalentTo(Value);
    }

    public void Dispose() => hostFixture.Dispose();
}
