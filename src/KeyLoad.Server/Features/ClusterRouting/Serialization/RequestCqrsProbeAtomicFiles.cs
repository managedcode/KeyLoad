using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>The original bounded atomic writer, borrowing the owning lock and quota snapshot.</summary>
internal static class RequestCqrsProbeAtomicFiles
{
    private const string TemporarySuffix = ".tmp";
    internal static void Write(string root, string destination, byte[] bytes, IOptions<RequestProbeExecutionOptions> executionOptions, Func<RequestCqrsProbeSnapshot> readSnapshot)
    {
        if (OperatingSystem.IsWindows())
        { throw Invalid(); }
        if (bytes.Length > executionOptions.Value.MaximumRecordBytes || File.Exists(destination))
        { throw Invalid(); }
        var before = readSnapshot();
        if (before.FileCount >= executionOptions.Value.MaximumFiles
            || before.AggregateBytes + bytes.Length > executionOptions.Value.MaximumAggregateBytes)
        { throw Invalid(); }
        var temporary = Path.Combine(root, RequestCqrsProbeProtocol.TemporaryFilePrefix + Guid.NewGuid().ToString(RequestCqrsProbeProtocol.SessionIdFormat) + TemporarySuffix);
        using (var stream = new FileStream(temporary, new FileStreamOptions
        {
            BufferSize = executionOptions.Value.FileBufferBytes,
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode,
            Options = FileOptions.WriteThrough
        }))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        var staged = readSnapshot();
        if (staged.FileCount > executionOptions.Value.MaximumFiles
            || staged.AggregateBytes > executionOptions.Value.MaximumAggregateBytes)
        { throw Invalid(); }
        File.Move(temporary, destination, overwrite: false);
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
