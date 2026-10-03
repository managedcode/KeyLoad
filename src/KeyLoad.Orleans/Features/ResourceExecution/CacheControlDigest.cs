using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.Digest), Immutable]
internal readonly record struct CacheControlDigest(
    [property: Id(0)] ulong Word0,
    [property: Id(1)] ulong Word1,
    [property: Id(2)] ulong Word2,
    [property: Id(3)] ulong Word3)
{
    internal const int ByteLength = 32;

    public static CacheControlDigest FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException("A digest requires exactly 32 bytes.", nameof(bytes));
        }

        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]));
    }

    public void WriteBytes(Span<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException("A digest requires exactly 32 bytes.", nameof(bytes));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Word0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], Word1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], Word2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], Word3);
    }

    public bool FixedTimeEquals(CacheControlDigest other)
    {
        Span<byte> left = stackalloc byte[ByteLength];
        Span<byte> right = stackalloc byte[ByteLength];
        WriteBytes(left);
        other.WriteBytes(right);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
