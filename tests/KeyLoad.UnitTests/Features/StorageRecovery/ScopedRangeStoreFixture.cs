using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ScopedRangeStoreFixture : IDisposable
{
    private const string TemporaryDirectoryPrefix = "keyload-scoped-range-";
    private const string GuidFormat = "N";
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));

    public ZoneTreeStore Store { get; }

    public ScopedRangeStoreFixture() => Store = new(new(directory));

    public void Dispose()
    {
        Store.Dispose();
        Directory.Delete(directory, true);
    }
}
