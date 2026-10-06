using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-CQ-034: observes configured mutable capacity on a genuine native ZoneTree owner.</summary>
[NotInParallel]
internal sealed class ScaledRawStorageExecutionPolicyTests
{
    private const int RecordCount = 4;
    private const int PayloadBytes = 32;

    [Test]
    [Arguments(1000, 1, null, 1002)]
    [Arguments(1000, 1, 1, 1001)]
    [Arguments(4, 1000, 1, 1000)]
    public async Task BoundSlackReachesNativeCapacityAndPreservesAllSeededValuesAndResidenceAsync(
        int recordCount, int minimumRecords, int? slackRecords, int expectedCapacity)
    {
        var options = Bind(minimumRecords, slackRecords);
        var corpus = new ScaledRawStorageCorpus(recordCount, PayloadBytes, options);
        var arena = new ScaledRawStorageValueArena(corpus, options);
        var engine = new ScaledRawStorageZoneTreeEngine(corpus, arena, new byte[PayloadBytes],
            Stopwatch.GetTimestamp(), options, TestContext.Current!.Execution.CancellationToken);
        string directory;
        using (engine)
        {
            engine.Initialize();
            directory = engine.Directory!;
            await Assert.That(Directory.Exists(directory)).IsTrue();
            await Assert.That(engine.CloneNativeOptions().MutableSegmentMaxItemCount).IsEqualTo(expectedCapacity);
            for (var index = 0; index < recordCount; index++)
            {
                engine.Upsert(index, arena.Value(index));
            }
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var index = 0; index < recordCount; index++)
            {
                await Assert.That(engine.TryRead(index, out var value)).IsTrue();
                digest.AppendData(value.Span);
            }
            await Assert.That(Convert.ToHexStringLower(digest.GetHashAndReset())).IsEqualTo(
                ScaledRawStorageFixtureOracleTests.IndependentValueDigest(recordCount, PayloadBytes));
            await Assert.That(engine.TryRead(recordCount, out _)).IsFalse();
            await Assert.That(engine.Capture().ResidentRecords).IsEqualTo((long)recordCount);
        }
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(3)]
    public async Task InvalidNativeSlackBindingRejectsBeforeOwnerFileIoAsync(int slackRecords)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => Bind(1, slackRecords));
        await Assert.That(failure.OptionsName).IsEqualTo(Options.DefaultName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ScaledStorageExecutionOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([ScaledStorageExecutionOptions.ValidationMessage]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(3)]
    public async Task StandaloneNativeOwnerRejectsInvalidSlackBeforeInitializationAsync(int slackRecords)
    {
        var validOptions = Bind(1, null);
        var corpus = new ScaledRawStorageCorpus(RecordCount, PayloadBytes, validOptions);
        var arena = new ScaledRawStorageValueArena(corpus, validOptions);
        var invalidOptions = Options.Create(new ScaledStorageExecutionOptions { MutableSegmentSlackRecords = slackRecords });
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => _ = new ScaledRawStorageZoneTreeEngine(
            corpus, arena, new byte[PayloadBytes], Stopwatch.GetTimestamp(), invalidOptions, CancellationToken.None));
        await Assert.That(failure.OptionsName).IsEqualTo(ScaledStorageExecutionOptions.SectionName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ScaledStorageExecutionOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([ScaledStorageExecutionOptions.ValidationMessage]);
    }

    private static IOptions<ScaledStorageExecutionOptions> Bind(int minimumRecords, int? slackRecords)
    {
        var input = new Dictionary<string, string?>
        {
            [ScaledStorageExecutionOptions.SectionName + ":" + nameof(ScaledStorageExecutionOptions.MinimumMutableSegmentRecords)]
                = minimumRecords.ToString(CultureInfo.InvariantCulture)
        };
        if (slackRecords is { } slack)
        {
            input[ScaledStorageExecutionOptions.SectionName + ":" + nameof(ScaledStorageExecutionOptions.MutableSegmentSlackRecords)]
                = slack.ToString(CultureInfo.InvariantCulture);
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(input).Build();
        using var lifetime = configuration as IDisposable;
        return BenchmarkScenarioOptionsRegistration.Read<ScaledStorageExecutionOptions>(configuration,
            ScaledStorageExecutionOptions.SectionName, static settings => settings.IsValid(),
            ScaledStorageExecutionOptions.ValidationMessage);
    }
}
