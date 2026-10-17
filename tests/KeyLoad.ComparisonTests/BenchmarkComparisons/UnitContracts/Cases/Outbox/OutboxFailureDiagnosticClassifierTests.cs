using System.Collections.Immutable;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OutboxFailureDiagnosticClassifierTests
{
    [Test]
    public async Task ExactResourceExhaustedFailureIsEligible()
    {
        var failed = Failed("KeyLoad:ResourceExhausted");

        await Assert.That(KeyLoadFailureDiagnostics.IsEligible(failed)).IsTrue();
    }

    [Test]
    public async Task SetupPrefixedExactResourceExhaustedFailureIsEligible()
    {
        var failed = Failed("setup:KeyLoad:ResourceExhausted");

        await Assert.That(KeyLoadFailureDiagnostics.IsEligible(failed)).IsTrue();
    }

    [Test]
    public async Task OtherCodesStatusesAndMalformedPrefixesAreIneligible()
    {
        var cases = new[]
        {
            Failed("KeyLoad:OwnershipLost"),
            Failed("setup:KeyLoad:OwnershipLost"),
            Failed("prefix:KeyLoad:ResourceExhausted"),
            Failed("KeyLoad:ResourceExhausted:StoredQuotaExhausted"),
            Failed("setup:KeyLoad:ResourceExhausted:StoredQuotaExhausted"),
            Failed("KeyLoad:ResourceExhausted", ComparisonStatuses.Measured),
            Failed("KeyLoad:ResourceExhausted", ComparisonStatuses.Unsupported),
            Failed(null),
            Failed("KeyLoad:ResourceExhausted", repetition: -1),
            Failed("KeyLoad:ResourceExhausted", scenario: (Scenario)int.MaxValue)
        };

        foreach (var failed in cases)
        {
            await Assert.That(KeyLoadFailureDiagnostics.IsEligible(failed)).IsFalse();
        }
    }

    [Test]
    public async Task ExactFailedSampleCodeQualifiesAnAlreadyFailedMeasuredCase()
    {
        var failed = Failed("Failed attempts remain in raw samples.") with
        {
            Measurement = new Measurement(1, 0, 1, 1, 0, new(1, 1, 1), 0, null, null, null),
            Samples = [new OperationSample(0, 0, 0, 1, false, "KeyLoad:ResourceExhausted", 12, null, null)]
        };

        await Assert.That(KeyLoadFailureDiagnostics.IsEligible(failed)).IsTrue();
    }

    [Test]
    public async Task SampleCodeDoesNotQualifyWithoutFailedSampleOrMeasurement()
    {
        var failedSample = new OperationSample(0, 0, 0, 1, false, "KeyLoad:ResourceExhausted", 12, null, null);
        var cases = new[]
        {
            Failed("Other failure") with { Samples = [failedSample] },
            Failed("Other failure") with
            {
                Measurement = new Measurement(1, 0, 1, 1, 0, new(1, 1, 1), 0, null, null, null),
                Samples = [failedSample with { Success = true }]
            }
        };

        foreach (var failed in cases)
        {
            await Assert.That(KeyLoadFailureDiagnostics.IsEligible(failed)).IsFalse();
        }
    }

    [Test]
    public async Task NullSampleDoesNotThrowOrQualify()
    {
        var failed = Failed("Other failure") with
        {
            Measurement = new Measurement(1, 0, 1, 1, 0, new(1, 1, 1), 0, null, null, null),
            Samples = ImmutableArray<OperationSample>.Empty.Add(null!)
        };

        await Assert.That(KeyLoadFailureDiagnostics.IsEligible(failed)).IsFalse();
    }

    [Test]
    public async Task CleanupFailureWithResourceExhaustedSampleIsIneligible()
    {
        var failed = Failed(ComparisonSessionCleanup.Failure) with
        {
            Measurement = new Measurement(1, 0, 1, 1, 0, new(1, 1, 1), 0, null, null, null),
            Samples = [new OperationSample(0, 0, 0, 1, false, "KeyLoad:ResourceExhausted", 12, null, null)]
        };

        await Assert.That(KeyLoadFailureDiagnostics.IsEligible(failed)).IsFalse();
    }

    private static ComparisonCase Failed(string? detail, string status = ComparisonStatuses.Failed,
        Scenario scenario = Scenario.DocumentUpdate, int repetition = 2)
        => new("KeyLoad", scenario, repetition, status, detail, null, ImmutableArray<OperationSample>.Empty);
}
