using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OutboxFailureDiagnosticFormatterTests
{
    private readonly KeyLoadOutboxDiagnosticLine diagnostics = new(UnitBenchmarkOptions.Diagnostics());

    private const string ConsumerCanary = "CONSUMER_NAME_PRIVATE_CANARY";
    private const string DefinitionCanary = "DEFINITION_PRIVATE_CANARY";
    private const string PartitionKey = "partition";

    [Test]
    public async Task TypedStatusFormatsOnlyClosedScenarioAndNumericFields()
    {
        var status = Status(new OutboxHead(17, 4, 14, 8192),
        [
            Consumer(ConsumerCanary, DefinitionCanary, checkpoint: 9, released: false),
            Consumer("released-name", "released-definition", checkpoint: 2, released: true),
            Consumer("second-active", "second-definition", checkpoint: 5, released: false)
        ]);

        var line = diagnostics.Format(Failed(Scenario.DocumentUpdate, 3), status);

        await Assert.That(line).Contains("scenario=DocumentUpdate");
        await Assert.That(line).Contains("repetition=3");
        await Assert.That(line).Contains("tail=17");
        await Assert.That(line).Contains("firstAvailable=4");
        await Assert.That(line).Contains("storedRecords=14");
        await Assert.That(line).Contains("storedBytes=8192");
        await Assert.That(line).Contains("activeConsumerCount=2");
        await Assert.That(line).Contains("minimumActiveCheckpoint=5");
        await Assert.That(line.Contains(ConsumerCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(line.Contains(DefinitionCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(line.Contains("released-name", StringComparison.Ordinal)).IsFalse();
        await Assert.That(Encoding.ASCII.GetByteCount(line)).IsLessThanOrEqualTo(512);
        await Assert.That(line.All(character => character is >= ' ' and <= '~')).IsTrue();
    }

    [Test]
    public async Task EmptyStatusUsesNoActiveConsumerSentinel()
    {
        var line = diagnostics.Format(Failed(Scenario.QueueCycle, 0),
            Status(new OutboxHead(0, 1, 0, 0), ImmutableArray<ProjectionConsumerInfo>.Empty));

        await Assert.That(line).Contains("activeConsumerCount=0");
        await Assert.That(line).Contains("minimumActiveCheckpoint=-1");
    }

    [Test]
    public async Task NegativeCountersAndUnclosedCaseNumbersAreRejected()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await Task.CompletedTask;
            _ = diagnostics.Format(Failed(Scenario.PointRead, 0),
                Status(new OutboxHead(-1, 0, 0, 0), ImmutableArray<ProjectionConsumerInfo>.Empty));
        });
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await Task.CompletedTask;
            _ = diagnostics.Format(Failed(Scenario.PointRead, -1),
                Status(new OutboxHead(0, 0, 0, 0), ImmutableArray<ProjectionConsumerInfo>.Empty));
        });
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await Task.CompletedTask;
            _ = diagnostics.Format(Failed((Scenario)int.MaxValue, 0),
                Status(new OutboxHead(0, 0, 0, 0), ImmutableArray<ProjectionConsumerInfo>.Empty));
        });
    }

    [Test]
    public async Task MaximumPublicCounterValuesStillFitTheOutputBudget()
    {
        var line = diagnostics.Format(Failed(Scenario.DocumentDelete, int.MaxValue),
            Status(new OutboxHead(long.MaxValue, 1, long.MaxValue, long.MaxValue),
                ImmutableArray<ProjectionConsumerInfo>.Empty));

        await Assert.That(Encoding.ASCII.GetByteCount(line)).IsLessThanOrEqualTo(512);
    }

    [Test]
    public async Task NumericFieldsAreInvariantAcrossCurrentCultures()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var customCulture = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
            customCulture.NumberFormat.NegativeSign = "NEG";
            CultureInfo.CurrentCulture = customCulture;
            var line = diagnostics.Format(Failed(Scenario.DocumentDelete, 12),
                Status(new OutboxHead(13, 4, 10, 8192), ImmutableArray<ProjectionConsumerInfo>.Empty));

            await Assert.That(line).Contains("repetition=12");
            await Assert.That(line).Contains("tail=13");
            await Assert.That(line).Contains("storedBytes=8192");
            await Assert.That(line).Contains("minimumActiveCheckpoint=-1");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public async Task DefaultAndOversizedConsumerCollectionsAreRejectedAsUnavailableInputs()
    {
        var head = new OutboxHead(1, 1, 1, 0);
        var failed = Failed(Scenario.DocumentUpdate, 0);
        var oversized = ImmutableArray.CreateRange(Enumerable.Range(0, 65)
            .Select(index => Consumer("consumer-" + index, "resource", 0, released: false)));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed, Status(head, default)));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed, Status(head, oversized)));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed, Status(null!, [])));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed, Status(new OutboxHead(0, 0, 0, 0), [])));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed, Status(new OutboxHead(1, 1, 0, 0), [])));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed, Status(new OutboxHead(1, 3, 0, 0), [])));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed,
            Status(head, ImmutableArray<ProjectionConsumerInfo>.Empty.Add(null!))));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed,
            Status(head, [Consumer("negative-checkpoint", "resource", -1, released: false)])));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed,
            Status(head, [Consumer("past-tail", "resource", 2, released: false)])));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => FormatAsync(failed,
            Status(head, [Consumer("released-past-tail", "resource", 2, released: true)])));
        await Assert.That(KeyLoadOutboxDiagnosticLine.UnavailableLine)
            .IsEqualTo("KeyLoadOutboxDiagnostic observation=unavailable");
    }

    private Task FormatAsync(ComparisonCase failed, OutboxStatus status)
    {
        _ = diagnostics.Format(failed, status);
        return Task.CompletedTask;
    }

    private static ComparisonCase Failed(Scenario scenario, int repetition)
        => new("KeyLoad", scenario, repetition, ComparisonStatuses.Failed, "KeyLoad:ResourceExhausted", null, []);

    private static OutboxStatus Status(OutboxHead head, ImmutableArray<ProjectionConsumerInfo> consumers)
        => new(head, consumers);

    private static ProjectionConsumerInfo Consumer(string name, string resource, long checkpoint, bool released)
        => new(new ProjectionConsumerRef(new PartitionRef("private", "database", "domain", PartitionKey), name),
            new ProjectionConsumerDefinition(1, [resource], [DefinitionCanary]), checkpoint, released);
}
