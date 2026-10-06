using System.Collections.Immutable;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveAppendValidationTests
{
    [Test]
    public async Task AcTsi002ConcurrentReceiptPermutationProvesCompleteBijection()
    {
        var commands = Enumerable.Range(0, 10000).Select(index =>
            TimeSeriesIntensiveReferenceOracle.CommandId("phase-run", 2, index)).ToImmutableArray();
        var validation = new TimeSeriesIntensiveAppendValidation(commands);
        Parallel.For(0, 10000, index => validation.Validate(index, new(commands[index], index * 7919L % 10000 + 1)));
        validation.ValidateComplete();
        var ledger = new TimeSeriesIntensiveAttemptLedger(new TimeSeriesIntensiveAttempt[10000], 0, 10000, 2);
        for (var index = 0; index < 10000; index++)
        {
            ledger.Publish(TimeSeriesIntensiveAttemptValues.Success(2, index) with { ReceiptSequence = index * 7919L % 10000 + 1 });
        }

        var view = new TimeSeriesIntensiveReceiptView(commands, ledger.Attempts);
        await Assert.That(view.Count).IsEqualTo(10000);
        for (var index = 0; index < 10000; index++)
        {
            await Assert.That(view[index]).IsEqualTo(new TimeSeriesIntensiveAppendReceipt(commands[index], index * 7919L % 10000 + 1));
        }
    }

    [Test]
    public void AcTsi002InvalidMissingAndDuplicateReceiptsFailWithoutReplacingValidSequence()
    {
        var command = Guid.NewGuid();
        var other = Guid.NewGuid();
        var validation = new TimeSeriesIntensiveAppendValidation([command, other]);
        Assert.ThrowsExactly<ComparisonFailureException>(validation.ValidateComplete);
        Assert.ThrowsExactly<ComparisonFailureException>(() => validation.Validate(0, new(other, 1)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => validation.Validate(0, new(command, 0)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => validation.Validate(0, new(command, 3)));
        validation.Validate(0, new(command, 2));
        Assert.ThrowsExactly<ComparisonFailureException>(() => validation.Validate(1, new(other, 2)));
        validation.Validate(1, new(other, 1));
        validation.ValidateComplete();
    }
}
