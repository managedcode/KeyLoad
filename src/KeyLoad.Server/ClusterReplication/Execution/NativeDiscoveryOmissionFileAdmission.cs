using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class NativeDiscoveryOmissionFileAdmission
{
    internal static T Locked<T>(string root, IOptions<RequestProbeExecutionOptions> options,
        Func<string, long> measure, Func<RequestCqrsProbeSnapshot> snapshot, Func<T> action)
    {
        if (OperatingSystem.IsWindows())
        {
            throw Invalid();
        }
        RequestCqrsProbePaths.RequireDirectory(root, root);
        var path = Path.Combine(root, NativeDiscoveryOmissionProtocol.LockFile);
        if (File.Exists(path))
        {
            _ = measure(path);
        }
        using var ownership = new FileStream(Path.Combine(root, NativeDiscoveryOmissionProtocol.LockFile), new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode,
            BufferSize = options.Value.FileBufferBytes
        });
        _ = snapshot();
        return action();
    }
    internal static long Measure(string path, IOptions<RequestProbeExecutionOptions> options)
    {
        var identity = KeyLoad.Storage.ZoneTree.IO.OfflineRegularFile.Inspect(path);
        RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
        if (identity.Length > options.Value.MaximumRecordBytes
            || Path.GetFileName(path) == NativeDiscoveryOmissionProtocol.LockFile && identity.Length != NativeDiscoveryOmissionProtocol.EmptyCount)
        {
            throw Invalid();
        }
        return identity.Length;
    }
    private const string TemporarySuffix = ".tmp";
    internal static bool Known(string name) => name is NativeDiscoveryOmissionProtocol.ArmFile
        or NativeDiscoveryOmissionProtocol.RequestFile or NativeDiscoveryOmissionProtocol.OmittedFile
        or NativeDiscoveryOmissionProtocol.VerifiedFile or NativeDiscoveryOmissionProtocol.LockFile
        or NativeSignedPurposeProtocol.ArmFile or NativeSignedPurposeProtocol.IssuedFile or NativeSignedPurposeProtocol.RejectedFile
        || name.StartsWith(RequestCqrsProbeProtocol.TemporaryFilePrefix, StringComparison.Ordinal)
            && name.EndsWith(TemporarySuffix, StringComparison.Ordinal)
            && Guid.TryParseExact(name.AsSpan(RequestCqrsProbeProtocol.TemporaryFilePrefix.Length,
                name.Length - RequestCqrsProbeProtocol.TemporaryFilePrefix.Length - TemporarySuffix.Length), RequestCqrsProbeProtocol.SessionIdFormat, out var id)
            && id != Guid.Empty;
    private static InvalidOperationException Invalid() => new(NativeDiscoveryOmissionProtocol.Invalid);
}
