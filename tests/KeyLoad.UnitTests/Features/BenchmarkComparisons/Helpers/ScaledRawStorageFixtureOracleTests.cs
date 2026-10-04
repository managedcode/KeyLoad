using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Independent value-digest and native-residency assertions for real scaled fixture cases.</summary>
internal static class ScaledRawStorageFixtureOracleTests
{
    private const int ValueHeaderBytes = 32;
    private const int MinimumPositiveProcessPeakBytes = 1;
    private const int DigestHexCharacters = 64;
    private const ulong SeedValue = 1729UL;
    private const int VersionValue = 1;
    private const int TailIndexMultiplier = 31;
    private const int TailOffsetMultiplier = 17;
    private const int TailIndexShiftOne = 8;
    private const int TailIndexShiftTwo = 16;

    internal static async Task AssertFixtureOrdersAsync(ScaledRawStorageFixture fixture, int recordCount)
    {
        var expectedOrder = new ScaledRawStorageReadOrder(recordCount);
        var mismatches = 0;
        for (var index = 0; index < recordCount; index++)
        {
            mismatches += fixture.ReadNextSequential() == (ulong)index ? 0 : 1;
            mismatches += fixture.ReadNextRandom() == (ulong)expectedOrder.NextRandom() ? 0 : 1;
        }

        await Assert.That(mismatches).IsEqualTo(0);
        await Assert.That(fixture.ReadNextSequential()).IsEqualTo(0UL);
        await Assert.That(fixture.ReadNextRandom()).IsEqualTo((ulong)FirstPermutationValue(recordCount));
    }

    internal static async Task AssertNativeResidenceAsync(
        ScaledRawStorageSnapshot snapshot, int expectedRecords)
    {
        await Assert.That(snapshot.ProcessPeakBytes >= MinimumPositiveProcessPeakBytes).IsTrue();
        await Assert.That(snapshot.NativeResidentRecords).IsNotNull();
        await Assert.That(Convert.ToInt64(snapshot.NativeResidentRecords, CultureInfo.InvariantCulture))
            .IsEqualTo(expectedRecords);
    }

    internal static string IndependentValueDigest(int recordCount, int payloadBytes)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var value = new byte[payloadBytes];
        for (var index = 0; index < recordCount; index++)
        {
            WriteReferenceValue(index, payloadBytes, value);
            digest.AppendData(value);
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    internal static bool IsSha256(string? value)
        => value is { Length: DigestHexCharacters } && value.All(Uri.IsHexDigit);

    private static int FirstPermutationValue(int recordCount)
    {
        var order = new ScaledRawStorageReadOrder(recordCount);
        return order.NextRandom();
    }

    private static void WriteReferenceValue(int index, int payloadBytes, Span<byte> value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(value, (ulong)index);
        BinaryPrimitives.WriteUInt64BigEndian(value[sizeof(ulong)..], SeedValue);
        BinaryPrimitives.WriteUInt32BigEndian(value[(sizeof(ulong) * 2)..], VersionValue);
        BinaryPrimitives.WriteUInt32BigEndian(value[((sizeof(ulong) * 2) + sizeof(uint))..], (uint)payloadBytes);
        BinaryPrimitives.WriteUInt64BigEndian(value[((sizeof(ulong) * 2) + (sizeof(uint) * 2))..], ~(ulong)index);
        for (var offset = ValueHeaderBytes; offset < payloadBytes; offset++)
        {
            value[offset] = unchecked((byte)((long)SeedValue + (index * (long)TailIndexMultiplier)
                + (offset * (long)TailOffsetMultiplier) + (index >> TailIndexShiftOne)
                + (index >> TailIndexShiftTwo)));
        }
    }
}
