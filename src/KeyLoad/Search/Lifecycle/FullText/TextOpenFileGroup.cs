using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Search;

internal sealed class TextOpenFileGroup(string path)
{
    private const long EmptyFileLength = 0;
    internal string? LinkedPath { get; set; } = path;
    internal bool Ambiguous
    {
        get; set;
    }
    internal Dictionary<IFileStream, TextOpenStream> Streams { get; } = new(ReferenceEqualityComparer.Instance);

    internal long ObserveLength()
    {
        var length = EmptyFileLength;
        foreach (var stream in Streams.Values)
        {
            var actual = stream.ObserveLength();
            if (actual < EmptyFileLength)
            {
                throw TextErrors.Corrupt();
            }
            length = Math.Max(length, actual);
        }
        return length;
    }
}
