using System.Security.Cryptography;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnHashStream(AnnSeedWork work, long maximumBytes) : Stream
{
    private const int Empty = 0;
    private const string Unsupported = "The native ANN digest stream is write-only.";
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long written;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => written;
    public override long Position { get => written; set => throw new NotSupportedException(Unsupported); }
    public override void Flush() => work.Check();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(Unsupported);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(Unsupported);
    public override void SetLength(long value) => throw new NotSupportedException(Unsupported);
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > maximumBytes - written)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        work.Charge(buffer.Length);
        hash.AppendData(buffer);
        written += buffer.Length;
    }

    internal byte[] Finish()
    {
        work.Check();
        if (written == Empty)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        return hash.GetHashAndReset();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        { hash.Dispose(); }
        base.Dispose(disposing);
    }
}
