using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed class TextOpenStream(IFileStream stream)
{
    private long? closingLength;
    internal long ObserveLength() => closingLength ?? stream.Length;
    internal void BeginJoinedClose() => closingLength ??= stream.Length;
}
