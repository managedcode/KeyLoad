using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOpenFileGroup(string path)
{
    private const long EmptyFileLength = 0;
    internal string? LinkedPath { get; set; } = path;
    internal bool Ambiguous { get; set; }
    internal Dictionary<IFileStream, NativeTextOpenStream> Streams { get; } = new(ReferenceEqualityComparer.Instance);

    internal long ObserveLength()
    {
        var length = EmptyFileLength;
        foreach (var stream in Streams.Values)
        {
            var actual = stream.ObserveLength();
            if (actual < EmptyFileLength)
            { throw NativeTextErrors.Corrupt(); }
            length = Math.Max(length, actual);
        }
        return length;
    }
}
