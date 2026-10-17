using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies the complete deterministic scale-v1 shuffle independently of its implementation.</summary>
internal sealed class ScaledRawStorageReadOrderTests
{
    private static readonly IOptions<ScaledStorageExecutionOptions> ExecutionOptions = UnitBenchmarkOptions.ScaledPreparation;

    private const int GoldenRecordCount = 10;
    private const int FullRecordCount = 100_000;
    private const int EncodedIndexBytes = 4;
    private const int InvalidOrderCount = 0;
    private const int MaximumOrderCount = 1_000_000;
    private const string ExpectedPermutationDigest = "27a19d3c4c492309aae1b627b59e84e68ef78f9b61ccab21a60813ae69d33789";
    private static readonly int[] ExpectedTenOrder = [3, 5, 0, 1, 2, 4, 8, 9, 7, 6];

    [Test]
    public async Task AcScale001TenRecordPermutationMatchesLiteralOrderAndDigest()
    {
        var order = new ScaledRawStorageReadOrder(GoldenRecordCount, ExecutionOptions);
        var actual = new int[GoldenRecordCount];
        for (var index = 0; index < actual.Length; index++)
        {
            actual[index] = order.NextRandom();
        }

        await Assert.That(actual).IsEquivalentTo(ExpectedTenOrder, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(order.PermutationDigest).IsEqualTo(ExpectedPermutationDigest);
        await Assert.That(order.NextRandom()).IsEqualTo(ExpectedTenOrder[0]);
        await Assert.That(order.RetainedOrderBytes).IsEqualTo((long)GoldenRecordCount * EncodedIndexBytes);
    }

    [Test]
    public async Task AcScale001ReadOrderRejectsCountsOutsideTheFrozenBound()
    {
        await Assert.That(() => new ScaledRawStorageReadOrder(InvalidOrderCount, ExecutionOptions))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ScaledRawStorageReadOrder(MaximumOrderCount + 1, ExecutionOptions))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AcScale001SequentialAndShuffledCursorsCoverEveryIndexAndWrapIndependently()
    {
        var order = new ScaledRawStorageReadOrder(FullRecordCount, ExecutionOptions);
        var visited = new bool[FullRecordCount];
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var encoded = new byte[EncodedIndexBytes];
        var invalidValues = 0;
        var duplicateValues = 0;

        for (var position = 0; position < FullRecordCount; position++)
        {
            var value = order.NextRandom();
            if ((uint)value >= FullRecordCount)
            {
                invalidValues++;
                continue;
            }

            if (visited[value])
            {
                duplicateValues++;
            }

            visited[value] = true;
            BinaryPrimitives.WriteInt32LittleEndian(encoded, value);
            digest.AppendData(encoded);
        }

        var missingValues = visited.Count(value => !value);
        await Assert.That(invalidValues).IsEqualTo(0);
        await Assert.That(duplicateValues).IsEqualTo(0);
        await Assert.That(missingValues).IsEqualTo(0);
        await Assert.That(visited.All(value => value)).IsTrue();
        await Assert.That(order.PermutationDigest).IsEqualTo(Convert.ToHexStringLower(digest.GetHashAndReset()));
        await Assert.That(order.RetainedOrderBytes).IsEqualTo((long)FullRecordCount * EncodedIndexBytes);
        await Assert.That(order.NextRandom()).IsEqualTo(FirstPermutationValue());

        var sequentialMismatches = 0;
        for (var index = 0; index < FullRecordCount; index++)
        {
            sequentialMismatches += order.NextSequential() == index ? 0 : 1;
        }

        await Assert.That(sequentialMismatches).IsEqualTo(0);
        await Assert.That(order.NextSequential()).IsEqualTo(0);
        await Assert.That(order.NextRandom()).IsEqualTo(SecondPermutationValue());
    }

    private static int FirstPermutationValue()
    {
        var order = new ScaledRawStorageReadOrder(FullRecordCount, ExecutionOptions);
        return order.NextRandom();
    }

    private static int SecondPermutationValue()
    {
        var order = new ScaledRawStorageReadOrder(FullRecordCount, ExecutionOptions);
        _ = order.NextRandom();
        return order.NextRandom();
    }
}
