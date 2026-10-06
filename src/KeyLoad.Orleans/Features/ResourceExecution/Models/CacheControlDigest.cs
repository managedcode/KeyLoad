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
    private const int SecondWordOffset = sizeof(ulong);
    private const int ThirdWordOffset = SecondWordOffset + sizeof(ulong);
    private const int FourthWordOffset = ThirdWordOffset + sizeof(ulong);

    public static CacheControlDigest FromBytes(ReadOnlySpan<byte> bytes)
    {
        const string FromBytesFailureMessage = "A digest requires exactly 32 bytes.";

        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException(FromBytesFailureMessage, nameof(bytes));
        }

        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[SecondWordOffset..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[ThirdWordOffset..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[FourthWordOffset..]));
    }

    public void WriteBytes(Span<byte> bytes)
    {
        const string WriteBytesFailureMessage = "A digest requires exactly 32 bytes.";

        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException(WriteBytesFailureMessage, nameof(bytes));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Word0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[SecondWordOffset..], Word1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[ThirdWordOffset..], Word2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[FourthWordOffset..], Word3);
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
