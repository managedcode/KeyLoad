using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class BackupArchivePermissionRegion : IAsyncDisposable
{
    private readonly string path;
    private readonly UnixFileMode originalMode;
    private readonly FileStream original;
    private bool restored;

    internal BackupArchivePermissionRegion(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The original archive access flow requires Unix file permissions.");
        }
        this.path = path;
        originalMode = File.GetUnixFileMode(path);
        original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    internal void DenyNewReaders()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The original archive access flow requires Unix file permissions.");
        }
        File.SetUnixFileMode(path, UnixFileMode.None);
    }

    internal void Repair()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The original archive access flow requires Unix file permissions.");
        }
        File.SetUnixFileMode(path, originalMode);
        restored = true;
    }

    internal async Task RequireOriginalAsync(byte[] expected, CancellationToken token)
    {
        original.Position = 0;
        var bytes = new byte[expected.Length];
        await original.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        await Assert.That(original.Length).IsEqualTo((long)expected.Length);
        await Assert.That(bytes.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (!restored)
        {
            ServerFailureObserver.Observe(Repair, failures);
        }
        try
        {
            await original.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
