using KeyLoad.Storage.ZoneTree.IO;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.SnapshotManifestEntryProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntryRefusal
{
    internal static async Task RunAsync(SnapshotManifestEntryOwners owner, SnapshotManifestEntryState state, string variant, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }
        var path = variant == P.Manifest ? state.ManifestPath : state.SourceImagePath;
        var bytes = variant == P.Manifest ? state.Manifest : state.SourceImage;
        var retained = Path.Combine(owner.Root, P.RetainedEntry);
        File.Move(path, retained);
        RequestCqrsProbeNativeFifo.Create(path);
        var original = OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path)
            : OfflineLinuxFileSystem.InspectPath(path);
        var mode = ReadUnixMode(path);
        await SnapshotManifestEntryChild.RunAsync(owner, variant, token);
        var current = OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path)
            : OfflineLinuxFileSystem.InspectPath(path);
        await Assert.That(current).IsEqualTo(original);
        await Assert.That(ReadUnixMode(path)).IsEqualTo(mode);
        await Assert.That((await File.ReadAllBytesAsync(retained, token)).AsSpan().SequenceEqual(bytes)).IsTrue();
        await SnapshotManifestEntrySeed.UnchangedAsync(owner, state, token);
        var other = variant == P.Manifest ? state.SourceImagePath : state.ManifestPath;
        var otherBytes = variant == P.Manifest ? state.SourceImage : state.Manifest;
        await Assert.That((await File.ReadAllBytesAsync(other, token)).AsSpan().SequenceEqual(otherBytes)).IsTrue();
        File.Delete(path);
        File.Move(retained, path);
        await Assert.That((await File.ReadAllBytesAsync(path, token)).AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    private static UnixFileMode ReadUnixMode(string path)
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException();
        }
        return File.GetUnixFileMode(path);
    }
}
