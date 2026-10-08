using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnStorageFrames
{
    internal const int CurrentVersion = 1;
    internal const int MaximumChunkBytes = 65_536;
    internal const string InvalidSnapshot = "The packed ANN native snapshot is invalid.";
    internal const string SnapshotExceeded = "The packed ANN native snapshot reservation is exceeded.";
    internal const int DigestBytes = 32;
    private const int Empty = 0;

    internal static void Write<T>(Stream destination, T value, PackedAnnStorageOptions options, AnnWorkBudget budget)
    {
        budget.Check();
        var measured = NativeSerialization.Measure(value);
        budget.Charge(measured);
        if (measured > MaximumChunkBytes || checked(destination.Position + sizeof(int) + measured) > options.MaxFileBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SnapshotExceeded); }
        var payload = NativeSerialization.Serialize(value);
        if (payload.Length != measured || payload.Length > MaximumChunkBytes
            || checked(destination.Position + sizeof(int) + payload.Length) > options.MaxFileBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SnapshotExceeded);
        }
        budget.Charge(payload.Length);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        destination.Write(length);
        destination.Write(payload);
        budget.Check();
    }

    internal static T Read<T>(Stream source, AnnWorkBudget budget)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        ReadExactly(source, length);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count <= Empty || count > MaximumChunkBytes || count > source.Length - source.Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshot);
        }
        budget.Charge(count);
        var payload = new byte[count];
        ReadExactly(source, payload);
        budget.Check();
        return NativeSerialization.Deserialize<T>(payload);
    }

    private static void ReadExactly(Stream source, Span<byte> target)
    {
        try
        {
            source.ReadExactly(target);
        }
        catch (EndOfStreamException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshot);
        }
    }

    internal static byte[] Digest(Stream source, PackedAnnStorageOptions options, AnnWorkBudget budget)
    {
        if (!source.CanRead || !source.CanSeek || source.Length <= Empty || source.Length > options.MaxFileBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshot);
        }
        PackedAnnStorageCodec.RequirePeak(MaximumChunkBytes, options);
        var position = source.Position;
        source.Position = Empty;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[MaximumChunkBytes];
        int read;
        while ((read = source.Read(buffer)) != Empty)
        {
            budget.Charge(read);
            hash.AppendData(buffer, Empty, read);
        }
        source.Position = position;
        return hash.GetHashAndReset();
    }

    internal static void RequireDigest(Stream source, byte[] expected, PackedAnnStorageOptions options, AnnWorkBudget budget)
    {
        if (expected.Length != DigestBytes || !CryptographicOperations.FixedTimeEquals(expected, Digest(source, options, budget)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSnapshot);
        }
    }
}
