using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class FilteredSearchCountingStream(int maximumBytes) : Stream
{
    private long bytes;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => bytes;
    public override long Position { get => bytes; set => throw new NotSupportedException(); }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Add(count);
    public override void Write(ReadOnlySpan<byte> buffer) => Add(buffer.Length);

    private void Add(int count)
    {
        if (count > maximumBytes - bytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, FilteredSearchErrors.RequestExceeded);
        }
        bytes += count;
    }
}
