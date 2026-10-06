using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Checks compact scale-v1 keys and complete binary value headers against literals.</summary>
internal sealed class ScaledRawStorageCorpusTests
{
    private static readonly IOptions<ScaledStorageExecutionOptions> ExecutionOptions = UnitBenchmarkOptions.ScaledPreparation;

    private const int CorpusRecordCount = 100_000;
    private const int ReservedMissIndex = CorpusRecordCount;
    private const int FirstAliasIndex = 1;
    private const int SecondAliasIndex = FirstAliasIndex + 256;
    private const int SmallArenaRecordCount = 3;
    private const int SecondArenaIndex = 1;
    private const int InvalidIndex = -1;
    private const int WriteOffsetIndex = 0;
    private const int FillByte = 0xA5;
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const int InvalidSmallPayloadBytes = 31;
    private const int InvalidLargePayloadBytes = 33;
    private const int InvalidCorpusRecordCount = 5_000_001;
    private const int ValueHeaderBytes = 32;
    private const int KeyBytes = 16;
    private const int TailOffset = 32;
    private const string KeyZeroHex = "00000000000006C10000000000000000";
    private const string KeyOneHex = "00000000000006C10000000000000001";
    private const string Key255Hex = "00000000000006C100000000000000FF";
    private const string Key256Hex = "00000000000006C10000000000000100";
    private const string Key65535Hex = "00000000000006C1000000000000FFFF";
    private const string Key65536Hex = "00000000000006C10000000000010000";
    private const string Key99999Hex = "00000000000006C1000000000001869F";
    private const string HeaderZero32 = "000000000000000000000000000006C10000000100000020FFFFFFFFFFFFFFFF";
    private const string HeaderOne32 = "000000000000000100000000000006C10000000100000020FFFFFFFFFFFFFFFE";
    private const string Header25532 = "00000000000000FF00000000000006C10000000100000020FFFFFFFFFFFFFF00";
    private const string Header25632 = "000000000000010000000000000006C10000000100000020FFFFFFFFFFFFFEFF";
    private const string Header65535_32 = "000000000000FFFF00000000000006C10000000100000020FFFFFFFFFFFF0000";
    private const string Header65536_32 = "000000000001000000000000000006C10000000100000020FFFFFFFFFFFEFFFF";
    private const string Header99999_32 = "000000000001869F00000000000006C10000000100000020FFFFFFFFFFFE7960";
    private const string HeaderZero1024 = "000000000000000000000000000006C10000000100000400FFFFFFFFFFFFFFFF";
    private const string HeaderOne1024 = "000000000000000100000000000006C10000000100000400FFFFFFFFFFFFFFFE";
    private const string Header2551024 = "00000000000000FF00000000000006C10000000100000400FFFFFFFFFFFFFF00";
    private const string Header2561024 = "000000000000010000000000000006C10000000100000400FFFFFFFFFFFFFEFF";
    private const string Header65535_1024 = "000000000000FFFF00000000000006C10000000100000400FFFFFFFFFFFF0000";
    private const string Header65536_1024 = "000000000001000000000000000006C10000000100000400FFFFFFFFFFFEFFFF";
    private const string Header99999_1024 = "000000000001869F00000000000006C10000000100000400FFFFFFFFFFFE7960";
    private const string UnchangedDestinationHex = "A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5A5";

    [Test]
    public async Task AcScale001KeysAreIndependentBigEndianIndexEncodings()
    {
        var corpus = new ScaledRawStorageCorpus(CorpusRecordCount, SmallPayloadBytes, ExecutionOptions);

        await Assert.That(Convert.ToHexString(corpus.Key(0).Span)).IsEqualTo(KeyZeroHex);
        await Assert.That(Convert.ToHexString(corpus.Key(1).Span)).IsEqualTo(KeyOneHex);
        await Assert.That(corpus.Key(FirstAliasIndex).Span.SequenceCompareTo(corpus.Key(256).Span) < 0).IsTrue();
        await Assert.That(corpus.Key(FirstAliasIndex).Span.SequenceEqual(corpus.Key(SecondAliasIndex).Span)).IsFalse();
        var firstAlias = new byte[SmallPayloadBytes];
        var secondAlias = new byte[SmallPayloadBytes];
        corpus.WriteValue(FirstAliasIndex, firstAlias);
        corpus.WriteValue(SecondAliasIndex, secondAlias);
        await Assert.That(firstAlias.AsSpan().SequenceEqual(secondAlias)).IsFalse();
        await Assert.That(corpus.Key(ReservedMissIndex).Length).IsEqualTo(KeyBytes);
        var reservedValue = new byte[SmallPayloadBytes];
        corpus.WriteValue(ReservedMissIndex, reservedValue);
        await Assert.That(reservedValue.Length).IsEqualTo(SmallPayloadBytes);
        await Assert.That(corpus.Key(255).Span.SequenceEqual(corpus.Key(511).Span)).IsFalse();
        await Assert.That(corpus.RetainedKeyBytes).IsEqualTo((long)(CorpusRecordCount + 1) * KeyBytes);
    }

    [Test]
    public async Task AcScale001First256IndicesHaveNoLowByteKeyOrValueAliases()
    {
        var corpus = new ScaledRawStorageCorpus(CorpusRecordCount, SmallPayloadBytes, ExecutionOptions);
        var firstValue = new byte[SmallPayloadBytes];
        var secondValue = new byte[SmallPayloadBytes];
        var collisions = 0;
        for (var index = 0; index < 256; index++)
        {
            collisions += corpus.Key(index).Span.SequenceEqual(corpus.Key(index + 256).Span) ? 1 : 0;
            corpus.WriteValue(index, firstValue);
            corpus.WriteValue(index + 256, secondValue);
            collisions += firstValue.AsSpan().SequenceEqual(secondValue) ? 1 : 0;
        }

        await Assert.That(collisions).IsEqualTo(0);
    }

    [Test]
    [Arguments(0, KeyZeroHex, HeaderZero32, HeaderZero1024, 0xE1, 0xB0)]
    [Arguments(1, KeyOneHex, HeaderOne32, HeaderOne1024, 0x00, 0xCF)]
    [Arguments(255, Key255Hex, Header25532, Header2551024, 0xC2, 0x91)]
    [Arguments(256, Key256Hex, Header25632, Header2561024, 0xE2, 0xB1)]
    [Arguments(65_535, Key65535Hex, Header65535_32, Header65535_1024, 0xC1, 0x90)]
    [Arguments(65_536, Key65536Hex, Header65536_32, Header65536_1024, 0xE2, 0xB1)]
    [Arguments(99_999, Key99999Hex, Header99999_32, Header99999_1024, 0xA9, 0x78)]
    public async Task AcScale001ValuesMatchIndependentCompleteKeyAndHeaderGoldens(
        int index, string expectedKey, string expected32, string expected1024, int expectedTail32, int expectedTail1023)
    {
        var small = new ScaledRawStorageCorpus(CorpusRecordCount, SmallPayloadBytes, ExecutionOptions);
        var large = new ScaledRawStorageCorpus(CorpusRecordCount, LargePayloadBytes, ExecutionOptions);
        await Assert.That(Convert.ToHexString(small.Key(index).Span)).IsEqualTo(expectedKey);
        var smallValue = new byte[SmallPayloadBytes];
        var largeValue = new byte[LargePayloadBytes];

        small.WriteValue(index, smallValue);
        large.WriteValue(index, largeValue);

        await Assert.That(Convert.ToHexString(smallValue)).IsEqualTo(expected32);
        await Assert.That(Convert.ToHexString(largeValue.AsSpan(0, ValueHeaderBytes))).IsEqualTo(expected1024);
        await Assert.That(largeValue[TailOffset]).IsEqualTo((byte)expectedTail32);
        await Assert.That(largeValue[^1]).IsEqualTo((byte)expectedTail1023);
    }

    [Test]
    public async Task AcScale001RejectsInvalidBoundsAndWrongDestinationLengthsBeforeWriting()
    {
        await Assert.That(() => new ScaledRawStorageCorpus(0, SmallPayloadBytes, ExecutionOptions))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ScaledRawStorageCorpus(InvalidCorpusRecordCount, SmallPayloadBytes, ExecutionOptions))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ScaledRawStorageReadOrder(0, ExecutionOptions)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ScaledRawStorageReadOrder(InvalidCorpusRecordCount, ExecutionOptions))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ScaledRawStorageCorpus(CorpusRecordCount, InvalidSmallPayloadBytes, ExecutionOptions))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ScaledRawStorageCorpus(CorpusRecordCount, InvalidLargePayloadBytes, ExecutionOptions))
            .Throws<ArgumentOutOfRangeException>();

        var corpus = new ScaledRawStorageCorpus(CorpusRecordCount, SmallPayloadBytes, ExecutionOptions);
        await Assert.That(() => corpus.Key(InvalidIndex)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => corpus.Key(ReservedMissIndex + 1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => corpus.WriteValue(InvalidIndex, new byte[SmallPayloadBytes]))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => corpus.WriteValue(ReservedMissIndex + 1, new byte[SmallPayloadBytes]))
            .Throws<ArgumentOutOfRangeException>();

        var wrongLength = Enumerable.Repeat((byte)FillByte, SmallPayloadBytes - 1).ToArray();
        await Assert.That(() => corpus.WriteValue(WriteOffsetIndex, wrongLength)).Throws<ArgumentException>();
        await Assert.That(Convert.ToHexString(wrongLength)).IsEqualTo(UnchangedDestinationHex[..(wrongLength.Length * 2)]);
        var oversized = Enumerable.Repeat((byte)FillByte, SmallPayloadBytes + 1).ToArray();
        await Assert.That(() => corpus.WriteValue(WriteOffsetIndex, oversized)).Throws<ArgumentException>();
        await Assert.That(Convert.ToHexString(oversized)).IsEqualTo(UnchangedDestinationHex + "A5");
    }

    [Test]
    public async Task AcScale001ValueArenaRetainsOnlySeededValuesAndDoesNotAliasGeneratorScratch()
    {
        var corpus = new ScaledRawStorageCorpus(SmallArenaRecordCount, SmallPayloadBytes, ExecutionOptions);
        var arena = new ScaledRawStorageValueArena(corpus, ExecutionOptions);
        var retained = arena.Value(SecondArenaIndex).ToArray();
        var scratch = new byte[SmallPayloadBytes];

        corpus.WriteValue(WriteOffsetIndex, scratch);
        Array.Fill(scratch, byte.MaxValue);

        await Assert.That(arena.RetainedValueBytes).IsEqualTo((long)SmallArenaRecordCount * SmallPayloadBytes);
        await Assert.That(arena.Value(SecondArenaIndex).Span.SequenceEqual(retained)).IsTrue();
        await Assert.That(() => arena.Value(SmallArenaRecordCount)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AcScale001RejectsCrossSupportedPayloadDestinationsWithoutWriting()
    {
        var small = new ScaledRawStorageCorpus(SmallArenaRecordCount, SmallPayloadBytes, ExecutionOptions);
        var large = new ScaledRawStorageCorpus(SmallArenaRecordCount, LargePayloadBytes, ExecutionOptions);
        var smallDestination = Enumerable.Repeat((byte)FillByte, SmallPayloadBytes).ToArray();
        var largeDestination = Enumerable.Repeat((byte)FillByte, LargePayloadBytes).ToArray();

        await Assert.That(() => small.WriteValue(0, largeDestination)).Throws<ArgumentException>();
        await Assert.That(() => large.WriteValue(0, smallDestination)).Throws<ArgumentException>();
        await Assert.That(smallDestination.All(value => value == FillByte)).IsTrue();
        await Assert.That(largeDestination.All(value => value == FillByte)).IsTrue();
    }

    [Test]
    public async Task AcScale003DataPreparationRejectsOriginalPreCancelledTokenBeforeAllocation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var corpus = new ScaledRawStorageCorpus(SmallArenaRecordCount, SmallPayloadBytes, ExecutionOptions);

        await Assert.That(() => new ScaledRawStorageCorpus(CorpusRecordCount, SmallPayloadBytes, ExecutionOptions, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(() => new ScaledRawStorageReadOrder(CorpusRecordCount, ExecutionOptions, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(() => new ScaledRawStorageValueArena(corpus, ExecutionOptions, cancellation.Token))
            .Throws<OperationCanceledException>();
    }
}
