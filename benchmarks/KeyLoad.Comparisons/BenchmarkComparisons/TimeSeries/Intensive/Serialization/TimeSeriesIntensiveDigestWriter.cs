using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveDigestWriter : IDisposable
{
    private const byte AbsentMarker = 0;
    private const byte PresentMarker = 1;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly int utf8StackBytes;
    private readonly IncrementalHash hash;

    internal TimeSeriesIntensiveDigestWriter(string domain)
        : this(domain, SerializationExecutionRegistration.Process)
    {
    }

    internal TimeSeriesIntensiveDigestWriter(string domain, IOptions<SerializationExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var configured = executionOptions.Value;
        configured.Validate();
        utf8StackBytes = configured.DigestUtf8StackBytes;
        hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Field(TimeSeriesIntensiveFrameLabels.Domain, domain);
        Field(TimeSeriesIntensiveFrameLabels.Version, TimeSeriesIntensiveFrameLabels.VersionValue);
    }

    internal void String(string value)
    {
        const int FirstElementIndex = 0;

        ArgumentNullException.ThrowIfNull(value);
        var length = Utf8.GetByteCount(value);
        Count(length);
        if (length <= utf8StackBytes)
        {
            Span<byte> buffer = stackalloc byte[utf8StackBytes];
            var written = Utf8.GetBytes(value, buffer);
            hash.AppendData(buffer[..written]);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var written = Utf8.GetBytes(value, rented);
            hash.AppendData(rented.AsSpan(FirstElementIndex, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal void Count(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)value);
        hash.AppendData(buffer);
    }

    internal void Integer(long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        hash.AppendData(buffer);
    }

    internal void Field(string label, string value)
    {
        String(label);
        String(value);
    }

    internal void Field(string label, long value)
    {
        String(label);
        Integer(value);
    }

    internal void Optional(string label, long? value)
    {
        String(label);
        Present(value.HasValue);
        if (value.HasValue)
        {
            Integer(value.Value);
        }
    }

    internal void Present(bool present)
    {
        const int FirstElementIndex = 0;

        Span<byte> buffer = stackalloc byte[sizeof(byte)];
        buffer[FirstElementIndex] = present ? PresentMarker : AbsentMarker;
        hash.AppendData(buffer);
    }

    internal string Finish() => Convert.ToHexStringLower(hash.GetHashAndReset());

    public void Dispose() => hash.Dispose();
}
