using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeReadCutFixture : IDisposable
{
    private const string DirectoryPrefix = "keyload-native-read-cut-";
    private const string Prefix = "doc/";
    private readonly List<ZoneTreeStore> opened = [];

    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
    internal ZoneTreeStore Store { get; private set; }

    internal NativeReadCutFixture()
    {
        Store = Open();
    }

    internal ZoneTreeReadCutLease Capture(ZoneTreeReadCutLimits limits, CancellationToken token = default)
        => Store.Read(view => Store.CaptureNativeReadCut(view, limits, token));

    internal void Reopen()
    {
        Store.Dispose();
        Store = Open();
    }

    internal static byte[] Key(string suffix) => Encoding.UTF8.GetBytes(Prefix + suffix);
    internal static byte[] Value(string value) => Encoding.UTF8.GetBytes(value);
    internal static ZoneTreeReadCutLimits Limits(int records, long bytes, TimeSpan? elapsed = null)
        => new(records, bytes, elapsed ?? TimeSpan.FromMinutes(1));

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var store in opened)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(store.Dispose, failures);
        }
        if (failures.Count == 0)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(() => Directory.Delete(DirectoryPath, recursive: true), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private ZoneTreeStore Open()
    {
        var store = new ZoneTreeStore(new(DirectoryPath));
        opened.Add(store);
        return store;
    }

}
