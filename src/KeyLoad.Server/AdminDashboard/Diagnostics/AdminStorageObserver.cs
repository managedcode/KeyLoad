using System.Security;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class AdminStorageObserver
{
    private const int MissingObservedFilesEmptyCount = 0;

    private const string Unavailable = "Node storage observation is unavailable.";

    internal static AdminStorageSnapshot Read(string directory, IOptions<AdminObservationOptions> options, CancellationToken cancellationToken, TimeProvider? clock = null)
    {
        const int EmptyRootAttributesFileAttributesReparsePoint = 0;

        cancellationToken.ThrowIfCancellationRequested();
        options.Value.Validate();
        var scan = new AdminStorageScan(rootPath: directory, cancellationToken: cancellationToken, options: options, clock: clock);
        try
        {
            var root = new DirectoryInfo(directory);
            if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != EmptyRootAttributesFileAttributesReparsePoint)
            {
                return Missing();
            }
            scan.Run(root);
            return scan.Snapshot();
        }
        catch (Exception error) when (FileFailure(error))
        {
            return Missing();
        }
    }

    internal static bool FileFailure(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException;

    private static AdminStorageSnapshot Missing() => new(null, null, null, null, MissingObservedFilesEmptyCount, false, [], Unavailable);
}
