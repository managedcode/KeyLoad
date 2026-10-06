using System.Text.Json;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Measures a whole typed SQL graph request without retaining serialized bytes.</summary>
internal static class SqlGraphSearchRequestSizer
{
    internal static void EnsureBounded(SqlGraphSearchRequest request, int maximumBytes, int maximumParameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.WrapperDetail);
        }
        if (request.Query.Parameters?.Count > maximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlGraphSearchSyntax.ParameterBudgetDetail);
        }

        using var counter = new BoundedCountingStream(maximumBytes, cancellationToken);
        try
        {
            JsonSerializer.Serialize(counter, request, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.WrapperDetail);
        }
        catch (InvalidOperationException)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.WrapperDetail);
        }
    }

    private sealed class BoundedCountingStream(int maximumBytes, CancellationToken cancellationToken) : Stream
    {
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Admit(count);
        public override void Write(ReadOnlySpan<byte> buffer) => Admit(buffer.Length);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Admit(buffer.Length);
            return ValueTask.CompletedTask;
        }

        private void Admit(int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            written = checked(written + count);
            if (written > maximumBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, SqlSyntax.ByteBudgetDetail);
            }
        }
    }
}
