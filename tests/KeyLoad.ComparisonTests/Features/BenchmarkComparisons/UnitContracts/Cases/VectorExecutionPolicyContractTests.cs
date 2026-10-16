using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorExecutionPolicyContractTests
{
    [Test]
    public async Task NativeDeadlineCancelsAnAwaitedOperationAndPreservesCallerCancellation()
    {
        var execution = SourcePolicy();
        execution.OperationTimeout = TimeSpan.ParseExact("00:00:00.0100000", "c", CultureInfo.InvariantCulture);
        using (var deadline = VectorOperationDeadline.Create(execution, TimeProvider.System, CancellationToken.None))
        {
            await Assert.That(async () => await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, deadline.Token))
                .Throws<TaskCanceledException>();
            await Assert.That(deadline.IsCancellationRequested).IsTrue();
        }
        execution = SourcePolicy();
        using var caller = new CancellationTokenSource();
        using var linked = VectorOperationDeadline.Create(execution, TimeProvider.System, caller.Token);
        await caller.CancelAsync();
        await Assert.That(async () => await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, linked.Token))
            .Throws<TaskCanceledException>();
        await Assert.That(linked.IsCancellationRequested).IsTrue();
    }

    [ConfigurationBinding]
    private static NativeComparisonExecutionOptions SourcePolicy()
    {
        var path = Path.Combine(SessionNodeProcess.RepositoryRoot(), "benchmarks", "KeyLoad.ComparisonHost",
            "Features", "BenchmarkComparisons", "Configuration", "native-execution.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var policy = configuration.GetRequiredSection(NativeComparisonExecutionOptions.SectionName)
            .Get<NativeComparisonExecutionOptions>()!;
        return policy.Validate();
    }
}
