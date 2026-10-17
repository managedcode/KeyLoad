using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Bounded memory summary of actual immutable row bytes; this is not a sorted-stream SHA256.</summary>
internal sealed class ResourceRetirementAuthorityAccumulator
{
    private const long InitialRecords = 0;
    private const long RecordStep = 1;
    private const int FirstByte = 0;
    private const int ByteStep = 1;
    private const string RecordDomain = "keyload.resource-retirement.authority-record.v1";
    private const string Capacity = "The native retirement authority accumulator is exhausted.";
    private readonly byte[] digest = new byte[ResourceRetirementAuthoritySummary.DigestBytes];
    private long records = InitialRecords;

    internal void Add(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (records == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity);
        }
        var hash = SHA256.HashData(KeyCodec.Encode(RecordDomain, key.ToArray(), value.ToArray()));
        for (var index = FirstByte; index < digest.Length; index += ByteStep)
        {
            digest[index] ^= hash[index];
        }
        records += RecordStep;
    }

    internal void Add(ResourceRetirementAuthoritySummary addition)
    {
        addition.RequireValid();
        if (addition.Records > long.MaxValue - records)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity);
        }
        for (var index = FirstByte; index < digest.Length; index += ByteStep)
        {
            digest[index] ^= addition.Digest.Span[index];
        }
        records += addition.Records;
    }

    internal ResourceRetirementAuthoritySummary Complete() => new(records, digest.ToArray());
}
