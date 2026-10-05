using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorExecutionPolicyContractTests
{
    [Test]
    public async Task RequiredSourcePolicyKeepsTheFrozenThirtySecondVectorDeadline()
    {
        var execution = SourcePolicy();
        var profile = VectorComparisonProfile.Parse("vector-100k-exact-plain-c16");
        await Assert.That(execution.OperationTimeout.TotalSeconds).IsEqualTo((double)profile.TimeoutSeconds);
        var runner = new VectorComparisonRunner(profile, Options.Create(execution));
        await Assert.That(runner).IsNotNull();
        execution.OperationTimeout = TimeSpan.ParseExact("00:00:00.0100000", "c", CultureInfo.InvariantCulture);
        await Assert.That(() => new VectorComparisonRunner(profile, Options.Create(execution))).Throws<ArgumentException>();
    }

    [Test]
    public async Task NativeDeadlineCancelsAnAwaitedOperationAndPreservesCallerCancellation()
    {
        var execution = SourcePolicy();
        execution.OperationTimeout = TimeSpan.ParseExact("00:00:00.0100000", "c", CultureInfo.InvariantCulture);
        using (var deadline = VectorOperationDeadline.Create(execution, CancellationToken.None))
        {
            await Assert.That(async () => await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token))
                .Throws<TaskCanceledException>();
            await Assert.That(deadline.IsCancellationRequested).IsTrue();
        }
        execution = SourcePolicy();
        using var caller = new CancellationTokenSource();
        using var linked = VectorOperationDeadline.Create(execution, caller.Token);
        await caller.CancelAsync();
        await Assert.That(async () => await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token))
            .Throws<TaskCanceledException>();
        await Assert.That(linked.IsCancellationRequested).IsTrue();
    }

    [ConfigurationBinding]
    private static NativeComparisonExecutionOptions SourcePolicy()
    {
        var path = Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "benchmarks", "KeyLoad.ComparisonHost",
            "Features", "BenchmarkComparisons", "Configuration", "native-execution.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var policy = configuration.GetRequiredSection(NativeComparisonExecutionOptions.SectionName)
            .Get<NativeComparisonExecutionOptions>()!;
        return policy.Validate();
    }
}
