namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeRecordFileFixture : IDisposable
{
    internal const string RecordName = "record.json";
    internal const string LinkName = "record-link.json";
    internal const string NestedDirectoryName = "nested";
    internal const string SocketName = "record.socket";
    internal const string FifoName = "record.pipe";
    internal const string OversizedName = "oversized.json";
    internal const string MaximumName = "maximum.json";
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private RequestCqrsProbeRecordFileFixture(string root) => Root = root;

    internal string Root { get; }

    internal static RequestCqrsProbeRecordFileFixture Create()
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Private mode fixtures require a Unix host."); }
        var root = Path.Combine(Path.GetTempPath(), $"keyload-c1-probe-unit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new RequestCqrsProbeRecordFileFixture(root);
    }

    internal string CreateRegular(string name, ReadOnlySpan<byte> bytes)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Private mode fixtures require a Unix host."); }
        var path = Path.Combine(Root, name);
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = PrivateFileMode
        });
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

}
