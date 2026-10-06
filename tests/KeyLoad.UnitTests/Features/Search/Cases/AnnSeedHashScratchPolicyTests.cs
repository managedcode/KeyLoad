using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>AC-CQ-034: bound ANN scratch reaches actual allocation, accounting and atomic hash blocks.</summary>
internal sealed class AnnSeedHashScratchPolicyTests
{
    private const string HashScratchKey = AnnSeedOptions.SectionName + ":" + nameof(AnnSeedOptions.HashScratchBytes);

    [Test]
    [Arguments(256)]
    [Arguments(257)]
    [Arguments(4096)]
    public async Task NativeBoundScratchFitsMaximumIdentifiersAndPreservesIndependentCorpusAndAccountingAsync(int scratchBytes)
    {
        using var database = AnnSeedTestSupport.Create(0);
        var id = new string('é', 128);
        var space = AnnSeedTestSupport.Space(dimension: 1025, id: new string('s', 256),
            model: new string('é', 128), version: new string('v', 256));
        var values = Enumerable.Range(0, 1025).Select(index => index + 0.25f).ToImmutableArray();
        database.Commit(new PutDocument(AnnSeedTestSupport.Collection, id, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, id, AnnSeedTestSupport.Field, values, space, 1));
        var configured = Capture(database, space, Bind(scratchBytes));
        var original = Capture(database, space, Bind(4096));
        var expected = new VectorRecord(id, AnnSeedTestSupport.Field, space, values, 1);
        var expectedDigest = AnnSeedDigestOracle.Compute(AnnSeedTestSupport.Field, space, [expected]);
        await Assert.That(configured.Records).HasSingleItem();
        await Assert.That(configured.Records.Single().DocumentId).IsEqualTo(id);
        await Assert.That(configured.Records.Single().Values.SequenceEqual(values)).IsTrue();
        await Assert.That(configured.CorpusSha256).IsEqualTo(expectedDigest);
        await Assert.That(original.CorpusSha256).IsEqualTo(expectedDigest);
        var expectedReservationDifference = 4096L - ((scratchBytes + 7L) & ~7L);
        await Assert.That(original.OwnedBytesUpperBound - configured.OwnedBytesUpperBound)
            .IsEqualTo(expectedReservationDifference);
        await Assert.That(original.PeakBytesUpperBound - configured.PeakBytesUpperBound)
            .IsEqualTo(expectedReservationDifference);
        var exact = Bind(scratchBytes).Value with
        {
            MaxOwnedBytes = configured.OwnedBytesUpperBound,
            MaxPeakBytes = configured.PeakBytesUpperBound
        };
        exact.Validate();
        var admitted = Capture(database, space, Options.Create(exact));
        await Assert.That(admitted.CorpusSha256).IsEqualTo(expectedDigest);
        var oneUnder = exact with { MaxOwnedBytes = exact.MaxOwnedBytes - 1 };
        oneUnder.Validate();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Capture(database, space, Options.Create(oneUnder)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(255)]
    [Arguments(4097)]
    public async Task NativeBindingRejectsUnsafeScratchBeforeCaptureAsync(int scratchBytes)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => Bind(scratchBytes));
        await Assert.That(failure.OptionsName).IsEqualTo(Options.DefaultName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(AnnSeedOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([AnnSeedOptions.ValidationMessage]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(255)]
    [Arguments(4097)]
    public async Task InvalidStandaloneScratchRejectsBeforeNativeReadAndWorkChargingAsync(int scratchBytes)
    {
        using var database = AnnSeedTestSupport.Create(1);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var options = Options.Create(new AnnSeedOptions { HashScratchBytes = scratchBytes });
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Capture(database, AnnSeedTestSupport.Space(), options, budget));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(failure.Message).IsEqualTo(AnnSeedOptions.ValidationMessage);
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
    }

    [Test]
    [Arguments(256)]
    [Arguments(257)]
    public async Task CanceledConfiguredCaptureRetainsActualTokenAndFollowingCaptureRemainsHealthyAsync(int scratchBytes)
    {
        using var database = AnnSeedTestSupport.Create(1);
        var options = Bind(scratchBytes);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: cancellation.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            Capture(database, AnnSeedTestSupport.Space(), options, budget));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
        await Assert.That(Capture(database, AnnSeedTestSupport.Space(), options).Records).HasSingleItem();
    }

    private static AnnSeed Capture(TestDatabase database, VectorSpace space, IOptions<AnnSeedOptions> options,
        ReadExecutionBudget? budget = null)
        => AnnSeedCollector.Capture(database.Database, AnnSeedTestSupport.Principal, database.Partition,
            AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field, space, options,
            budget ?? new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)));

    private static IOptions<AnnSeedOptions> Bind(int scratchBytes)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>(HashScratchKey, scratchBytes.ToString(CultureInfo.InvariantCulture))]).Build();
        using var lifetime = configuration as IDisposable;
        var services = new ServiceCollection();
        CoreRuntimeOptionsRegistration.AddCoreRuntimeOptions(services, configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AnnSeedOptions>>();
        _ = options.Value;
        return options;
    }
}
