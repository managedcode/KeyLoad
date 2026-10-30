using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Runs the session model operations against actual one-member and three-member database deployments.</summary>
[NotInParallel]
internal sealed class ModelDatabaseTests
{
    private const int Records = 30, Operations = 8, Clients = 4;

    [Test, Arguments(1), Arguments(3)]
    public Task QdrantModelsRetainVerifiedOperationsAndHealthyContinuation(int nodes) => RunAsync(IsolatedHostConstants.Qdrant, nodes);

    [Test, Arguments(1), Arguments(3)]
    public Task RabbitModelsRetainVerifiedOperationsAndHealthyContinuation(int nodes) => RunAsync(IsolatedHostConstants.Rabbit, nodes);

    [Test, Arguments(1), Arguments(3)]
    public Task KurrentModelsRetainVerifiedOperationsAndHealthyContinuation(int nodes) => RunAsync(IsolatedHostConstants.Kurrent, nodes);

    [Test, Arguments(1), Arguments(3)]
    public Task MongoModelsRetainVerifiedOperationsAndHealthyContinuation(int nodes) => RunAsync(NativeDatabaseTests.MongoDb, nodes);

    private static Task RunAsync(string target, int nodes)
        => AdditionalDatabaseResources.RunAsync(target, (database, token) => VerifyAsync(database, nodes, token),
            TestContext.Current!.Execution.CancellationToken, nodes);

    private static async Task VerifyAsync(IComparisonTarget target, int nodes, CancellationToken token)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, ComparisonExecutionRegistration.ConfigurationPath), optional: false);
        var options = new BenchmarkSessionOptions { Records = Records, Operations = Operations, Concurrency = Clients };
        var session = new BenchmarkSession(target, ComparisonExecutionRegistration.Read(configuration), options, development: true);
        foreach (var id in new[] { BenchmarkSessionModelCases.Prepare, BenchmarkSessionModelCases.Vector, BenchmarkSessionModelCases.Queue,
            BenchmarkSessionModelCases.Append, BenchmarkSessionModelCases.Read, BenchmarkSessionModelCases.Mixed })
        {
            var report = await session.ModelAsync(id, token);
            var supported = id switch
            {
                BenchmarkSessionModelCases.Prepare => true,
                BenchmarkSessionModelCases.Vector => target.Supports(Scenario.VectorExact),
                BenchmarkSessionModelCases.Queue => target.Supports(Scenario.QueueCycle),
                _ => target.Supports(Scenario.StreamAppend)
            };
            await Assert.That(report.Status).IsEqualTo(supported ? BenchmarkSessionProtocol.Measured : BenchmarkSessionProtocol.Unavailable);
            if (supported)
            {
                await VerifyCountsAsync(target, report);
                await Assert.That(report.VerifiedRecords).IsEqualTo(id == BenchmarkSessionModelCases.Prepare
                    ? BenchmarkSessionLimits.NoObservedItems : Operations);
                foreach (var lane in report.Lanes)
                {
                    await Assert.That(lane.Measurement.Successes).IsEqualTo(Operations);
                    await Assert.That(lane.Samples.Length).IsEqualTo(Operations);
                    await Assert.That(lane.Samples.All(item => item.Success)).IsTrue();
                }
            }
        }
        var continuation = target.Supports(Scenario.StreamRead) ? BenchmarkSessionModelCases.Read
            : target.Supports(Scenario.QueueCycle) ? BenchmarkSessionModelCases.Queue : BenchmarkSessionModelCases.Vector;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.That((await session.ModelAsync(continuation, cancelled.Token)).Status).IsEqualTo(BenchmarkSessionProtocol.Failed);
        await Assert.That((await session.ModelAsync(continuation, token)).Status).IsEqualTo(BenchmarkSessionProtocol.Measured);
        await Assert.That(target.Profile.Cluster!.Nodes).IsEqualTo(nodes);
    }

    private static async Task VerifyCountsAsync(IComparisonTarget target, BenchmarkSessionCaseReport report)
    {
        var initial = report.CaseId switch
        {
            BenchmarkSessionModelCases.Prepare or BenchmarkSessionModelCases.Queue => 0,
            BenchmarkSessionModelCases.Read or BenchmarkSessionModelCases.Mixed => Records + Operations,
            _ => Records
        };
        var expected = report.CaseId switch
        {
            BenchmarkSessionModelCases.Prepare => target.Supports(Scenario.VectorExact) || target.Supports(Scenario.StreamAppend) ? Records : 0,
            BenchmarkSessionModelCases.Queue => 0,
            BenchmarkSessionModelCases.Append or BenchmarkSessionModelCases.Read => Records + Operations,
            BenchmarkSessionModelCases.Mixed => Records + 2 * Operations,
            _ => Records
        };
        await Assert.That(report.InitialRecords).IsEqualTo(initial);
        await Assert.That(report.ExpectedRecords).IsEqualTo(expected);
    }
}
