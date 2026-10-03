using System.Runtime.CompilerServices;
using System.Text;
using Orleans.Serialization.Session;

namespace KeyLoad.Replication;

// Candidate-only, native admission/counting profile. The normal codec retains generated writers.
internal sealed class ReplicaInspectionBuffers(ReadOnlyMemory<byte> input, int maximumEntries, bool counting = false,
    string? expectedSender = null)
{
    private const char StringProxyCharacter = 'p';
    private const int StringProxyLength = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ConditionalWeakTable<SerializerSession, ReplicaInspectionBuffers> Scopes = new();
    private readonly Dictionary<string, ReadOnlyMemory<byte>> strings = new(ReferenceEqualityComparer.Instance);
    private readonly byte[]? senderUtf8 = expectedSender is null ? null : StrictUtf8.GetBytes(expectedSender);
    internal int MaximumEntries { get; } = maximumEntries;
    internal bool Counting { get; } = counting;

    internal static IDisposable Bind(SerializerSession session, ReplicaInspectionBuffers scope)
    {
        Scopes.Add(session, scope);
        return new Binding(session);
    }

    internal static ReplicaInspectionBuffers For(SerializerSession session) => Scopes.TryGetValue(session, out var scope)
        ? scope : throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);

    internal ReadOnlyMemory<byte> Borrow(long position, uint length) => input.Slice(checked((int)position), checked((int)length));

    internal string String(ReadOnlyMemory<byte> utf8)
    {
        _ = StrictUtf8.GetCharCount(utf8.Span);
        var proxy = new string(StringProxyCharacter, StringProxyLength);
        strings.Add(proxy, utf8);
        return proxy;
    }

    internal ReadOnlyMemory<byte> Utf8(string value) => strings.TryGetValue(value, out var utf8)
        ? utf8 : throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);

    internal string Metadata(string value, int maximumUtf8Bytes)
    {
        var utf8 = Utf8(value);
        if (utf8.Length > maximumUtf8Bytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        return StrictUtf8.GetString(utf8.Span);
    }

    internal int Utf16Length(string value) => StrictUtf8.GetCharCount(Utf8(value).Span);

    internal string Sender(string? value)
    {
        if (value is null)
        {
            throw Errors.Fail(senderUtf8 is null ? ErrorCode.Corruption : ErrorCode.PermissionDenied,
                ReplicaNativeOperationAdmission.SenderMismatch);
        }
        if (senderUtf8 is not null && !Utf8(value).Span.SequenceEqual(senderUtf8))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaNativeOperationAdmission.SenderMismatch);
        }
        return value;
    }

    internal ReplicaInspectionBuffers Counter()
    {
        var result = new ReplicaInspectionBuffers(input, MaximumEntries, counting: true);
        foreach (var pair in strings)
        {
            result.strings.Add(pair.Key, pair.Value);
        }
        return result;
    }

    private sealed class Binding(SerializerSession session) : IDisposable
    {
        public void Dispose() => Scopes.Remove(session);
    }
}

internal sealed record ReplicaBorrowedBytes(ReadOnlyMemory<byte> Bytes);
