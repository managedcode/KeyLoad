using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-CQ-034: bound Mongo policy reaches execution settings and replica deadline owners.</summary>
internal sealed class ComparisonMongoPolicyTests
{
    private const string Connection = "mongodb://localhost:27017/?replicaSet=benchmark";
    private const string InvalidConnection = "not-a-mongodb-connection";

    [Test]
    [Arguments(1, 16)]
    [Arguments(16, 20)]
    [Arguments(128, 132)]
    public async Task DefaultPoolPolicyPreservesMajorityJournalSettingsAsync(int concurrency, int expectedMaximum)
    {
        var settings = MongoTarget.CreateSettings(Connection, concurrency, UnitBenchmarkOptions.Execution());
        await Assert.That(settings.MaxConnectionPoolSize).IsEqualTo(expectedMaximum);
        await Assert.That(settings.RetryWrites).IsFalse();
        await Assert.That(settings.RetryReads).IsFalse();
        await Assert.That(settings.WriteConcern.Journal).IsTrue();
        await Assert.That(settings.WriteConcern.W.ToString()).IsEqualTo(MongoSchema.MajorityMode);
        await Assert.That(settings.ReadConcern).IsEqualTo(ReadConcern.Majority);
        await Assert.That(settings.ReadPreference).IsEqualTo(ReadPreference.Primary);
    }

    [Test]
    [Arguments("1", "1", 1, 2)]
    [Arguments("1", "3", 1, 3)]
    [Arguments("1", "3", 16, 17)]
    public async Task BoundLowerPoolPolicyReachesSettingsAndEvidenceAsync(string margin, string minimum,
        int concurrency, int expectedMaximum)
    {
        var options = BindPoolPolicy(margin, minimum);
        var settings = MongoTarget.CreateSettings(Connection, concurrency, options);
        var evidence = new Dictionary<string, string>();
        options.Value.RecordEvidence(evidence);
        await Assert.That(settings.MaxConnectionPoolSize).IsEqualTo(expectedMaximum);
        await Assert.That(evidence[nameof(ComparisonExecutionOptions.MongoPoolSessionMargin)]).IsEqualTo(margin);
        await Assert.That(evidence[nameof(ComparisonExecutionOptions.MongoPoolMinimumSize)]).IsEqualTo(minimum);
    }

    [Test]
    [Arguments("0", "16")]
    [Arguments("-1", "16")]
    [Arguments("5", "16")]
    [Arguments("4", "0")]
    [Arguments("4", "-1")]
    [Arguments("4", "17")]
    public async Task InvalidBoundPoolPolicyRejectsBeforeParsingConnectionAsync(string margin, string minimum)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(
            () => MongoTarget.CreateSettings(InvalidConnection, 1, BindPoolPolicy(margin, minimum)));
        await Assert.That(failure.OptionsName).IsEqualTo(ComparisonExecutionOptions.SectionName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ComparisonExecutionOptions));
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(int.MaxValue)]
    public async Task InvalidOrOverflowingConcurrencyRejectsBeforeConnectionParsingAsync(int concurrency)
    {
        var failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => MongoTarget.CreateSettings(InvalidConnection, concurrency, UnitBenchmarkOptions.Execution()));
        await Assert.That(failure.ParamName).IsEqualTo("concurrency");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfiguredReplicaOperationAndCleanupDeadlinesCancelRealPendingWorkAsync(bool cleanup)
    {
        var options = UnitBenchmarkOptions.Execution();
        if (cleanup)
        {
            options.Value.CleanupTimeout = TimeSpan.FromMilliseconds(10);
        }
        else
        {
            options.Value.OperationTimeout = TimeSpan.FromMilliseconds(10);
        }
        options.Value.Validate();
        using var deadline = cleanup
            ? MongoReplicaDeadline.CreateCleanup(options, TimeProvider.System)
            : MongoReplicaDeadline.CreateOperation(options, TimeProvider.System, CancellationToken.None);
        var pending = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, deadline.Token);
        var failure = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.CancellationToken).IsEqualTo(deadline.Token);
        await Assert.That(deadline.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task CallerCancellationStopsReplicaObservationAndRetainsIndependentCleanupAsync()
    {
        var options = UnitBenchmarkOptions.Execution();
        using var caller = new CancellationTokenSource();
        using var operation = MongoReplicaDeadline.CreateOperation(options, TimeProvider.System, caller.Token);
        using var cleanup = MongoReplicaDeadline.CreateCleanup(options, TimeProvider.System);
        var pending = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, operation.Token);
        await caller.CancelAsync();
        var failure = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending);
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.CancellationToken).IsEqualTo(operation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, cleanup.Token);
        await Assert.That(cleanup.IsCancellationRequested).IsFalse();
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(false, -1)]
    [Arguments(true, 0)]
    [Arguments(true, -1)]
    public async Task InvalidDeadlineRejectsBeforeCreatingCancellationOwnerAsync(bool cleanup, int milliseconds)
    {
        var options = UnitBenchmarkOptions.Execution();
        if (cleanup)
        {
            options.Value.CleanupTimeout = TimeSpan.FromMilliseconds(milliseconds);
        }
        else
        {
            options.Value.OperationTimeout = TimeSpan.FromMilliseconds(milliseconds);
        }
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() =>
        {
            using var deadline = cleanup
                ? MongoReplicaDeadline.CreateCleanup(options, TimeProvider.System)
                : MongoReplicaDeadline.CreateOperation(options, TimeProvider.System, CancellationToken.None);
        });
        await Assert.That(failure.OptionsName).IsEqualTo(ComparisonExecutionOptions.SectionName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ComparisonExecutionOptions));
    }

    private static OptionsManager<ComparisonExecutionOptions> BindPoolPolicy(string margin, string minimum)
    {
        var source = new Dictionary<string, ComparisonExecutionOptions>
        {
            [ComparisonExecutionOptions.SectionName] = UnitBenchmarkOptions.Execution().Value
        };
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(source));
        using var configuration = new ConfigurationManager();
        configuration.AddJsonStream(stream).AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ComparisonExecutionOptions.SectionName + ":" + nameof(ComparisonExecutionOptions.MongoPoolSessionMargin)] = margin,
                [ComparisonExecutionOptions.SectionName + ":" + nameof(ComparisonExecutionOptions.MongoPoolMinimumSize)] = minimum
            });
        var options = new OptionsManager<ComparisonExecutionOptions>(
            new OptionsFactory<ComparisonExecutionOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonExecutionOptions>(
                    configuration.GetRequiredSection(ComparisonExecutionOptions.SectionName))], [],
                [new ValidateOptions<ComparisonExecutionOptions>(Options.DefaultName,
                    value => { value.Validate(); return true; }, "The execution Mongo policy is invalid.")]));
        _ = options.Value;
        return options;
    }
}
