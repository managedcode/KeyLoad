using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[NotInParallel("DatabaseFlows")]
internal sealed class DocumentComparisonTests
{
    private const long NoObservedOperations = 0;
    private const long CancellationAcknowledgements = 20;
    private const int FirstRecord = 0;
    private const int SingleClient = 1;
    private const int OrdinaryClients = 16;
    private const int CancellationThreshold = 20;
    private const int SmallCorpusRecords = 100;
    private const int MaximumIdentityExclusive = 101;
    private const string MeasuredStatus = "measured";
    private const string FailedStatus = "failed";
    private const string SurrealTarget = "SurrealDB";
    private const string SurrealVersionPath = "version";
    private const string SurrealBuildVersion = "surrealdb-3.2.4+20260803.93ab219";

    [Test]
    [Arguments("SurrealDB")]
    [Arguments("HelixDB")]
    public async Task ControlledSessionRetainsOneCorpusAcrossLoadReadsSearchAndConcurrentMutations(string name)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await DatabaseFlowFixture.CreateAsync(name, token);
        await using var target = await OpenTargetAsync(fixture, name, token);
        var options = new BenchmarkSessionOptions { Records = 600, Operations = 100, Concurrency = OrdinaryClients };
        var session = new BenchmarkSession(target, DatabaseFlowFixture.ExecutionOptions, options, development: true);
        var load = await session.LoadAsync(token);
        await Assert.That(load.Status).IsEqualTo(MeasuredStatus);
        await Assert.That(load.VerifiedRecords).IsEqualTo(options.Records);
        await Assert.That(load.Lanes.Single().Measurement.Attempts).IsEqualTo(options.Records);
        var index = await session.IndexAsync(token);
        await Assert.That(index.Status is MeasuredStatus or "unavailable").IsTrue().Because(index.Detail ?? index.CaseId);
        foreach (var read in new[] { await session.SequentialReadAsync(token), await session.RandomReadAsync(token) })
        {
            await Assert.That(read.Status).IsEqualTo(MeasuredStatus).Because(read.Detail ?? read.CaseId);
            await Assert.That(read.Lanes.Single().Measurement.Successes).IsEqualTo(options.Operations);
            await Assert.That(read.Lanes.Single().Samples.All(sample => sample.Success)).IsTrue();
        }
        var search = await session.SearchAsync(token);
        await Assert.That(search.Status is MeasuredStatus or "unavailable").IsTrue().Because(search.Detail ?? search.CaseId);
        var mixed = await session.MixedAsync(token);
        await Assert.That(mixed.Status).IsEqualTo(MeasuredStatus).Because(mixed.Detail ?? mixed.CaseId);
        await Assert.That(mixed.Lanes.Select(lane => lane.Id)).Contains("create");
        await Assert.That(mixed.Lanes.Select(lane => lane.Id)).Contains("delete");
        await Assert.That(mixed.Lanes.All(lane => lane.Measurement.Successes == options.Operations)).IsTrue();
        var final = await session.VerifyAsync(token);
        await Assert.That(final.Status).IsEqualTo(MeasuredStatus).Because(final.Detail ?? final.CaseId);
        await Assert.That(final.VerifiedRecords).IsEqualTo(options.Records);
    }

    [Test]
    [Arguments("SurrealDB")]
    [Arguments("HelixDB")]
    public async Task CancellationRetainsActualAcknowledgementsAndPoisonedSessionWhileNewScopeRemainsHealthy(string name)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await DatabaseFlowFixture.CreateAsync(name, token);
        await using var target = await OpenTargetAsync(fixture, name, token);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var observed = NoObservedOperations;
        var options = new BenchmarkSessionOptions { Records = 6000, Operations = 1000, Concurrency = OrdinaryClients };
        var session = new BenchmarkSession(target, DatabaseFlowFixture.ExecutionOptions, options, development: true,
            progress: progress =>
            {
                Interlocked.Exchange(ref observed, progress.Acknowledged);
                if (progress.Acknowledged >= CancellationThreshold)
                {
                    canceled.Cancel();
                }
            });
        var failed = await session.LoadAsync(canceled.Token);
        await Assert.That(observed).IsGreaterThanOrEqualTo(CancellationAcknowledgements);
        await Assert.That(failed.Status).IsEqualTo(FailedStatus);
        var actual = failed.Lanes.Single().Measurement;
        await Assert.That(actual.Attempts).IsEqualTo(actual.Successes + actual.Failures);
        await Assert.That(actual.Successes).IsGreaterThanOrEqualTo(CancellationThreshold);
        await Assert.That((long)failed.Lanes.Single().Samples.Length).IsEqualTo(actual.Attempts);
        await Assert.That((await session.SequentialReadAsync(token)).Status).IsEqualTo(FailedStatus);
        await using var healthyTarget = await OpenTargetAsync(fixture, name, token);
        var healthy = new BenchmarkSession(healthyTarget, DatabaseFlowFixture.ExecutionOptions,
            new BenchmarkSessionOptions { Records = 600, Operations = 100, Concurrency = OrdinaryClients }, development: true);
        var next = await healthy.LoadAsync(token);
        await Assert.That(next.Status).IsEqualTo(MeasuredStatus).Because(next.Detail ?? next.CaseId);
        await Assert.That(next.VerifiedRecords).IsEqualTo(600);
    }

    [Test]
    [Arguments("SurrealDB")]
    [Arguments("HelixDB")]
    public async Task RealExtraMissingAndChangedBodiesRejectBeforeHealthyCorrectedReadback(string name)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await DatabaseFlowFixture.CreateAsync(name, token);
        if (name == SurrealTarget)
        {
            using var deadline = new CancellationTokenSource(DatabaseFlowFixture.ExecutionOptions.Value.OperationTimeout, TimeProvider.System);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            using var http = await fixture.ClientAsync(operation.Token);
            using var response = await http.GetAsync(new Uri(SurrealVersionPath, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, operation.Token);
            response.EnsureSuccessStatusCode();
            var version = await ComparisonResponse.ReadTextAsync(response.Content, DatabaseFlowFixture.ExecutionOptions.Value, operation.Token);
            await Assert.That(version).IsEqualTo(SurrealBuildVersion);
        }

        await using var target = await OpenTargetAsync(fixture, name, token);
        var execution = (IDocumentComparisonTarget)target;
        var corpus = new DocumentComparisonCorpus(SmallCorpusRecords, SingleClient);
        await execution.InitializeDocumentsAsync(new(corpus, true, MaximumIdentityExclusive), token);
        await using var session = await execution.OpenDocumentSessionAsync(token);
        await session.ExecuteAsync(Scenario.DocumentWrite, corpus.CreateDocument(SmallCorpusRecords), token);
        await Assert.That(async () => await DocumentComparisonOracle.VerifyAsync(session.ReadDocumentsAsync(token), corpus.Documents, token))
            .Throws<ComparisonFailureException>();
        await session.ExecuteAsync(Scenario.DocumentDelete, corpus.CreateDocument(SmallCorpusRecords), token);
        await session.ExecuteAsync(Scenario.DocumentDelete, corpus.CreateDocument(FirstRecord), token);
        await Assert.That(async () => await DocumentComparisonOracle.VerifyAsync(session.ReadDocumentsAsync(token), corpus.Documents, token))
            .Throws<ComparisonFailureException>();
        await session.ExecuteAsync(Scenario.DocumentWrite, corpus.CreateDocument(FirstRecord), token);
        await session.ExecuteAsync(Scenario.DocumentUpdate, corpus.Replacement(SingleClient), token);
        await Assert.That(async () => await DocumentComparisonOracle.VerifyAsync(session.ReadDocumentsAsync(token), corpus.Documents, token))
            .Throws<ComparisonFailureException>();
        await session.ExecuteAsync(Scenario.DocumentUpdate, corpus.CreateDocument(SingleClient), token);
        var healthy = await DocumentComparisonOracle.VerifyAsync(session.ReadDocumentsAsync(token), corpus.Documents, token);
        await Assert.That(healthy.Records).IsEqualTo(SmallCorpusRecords);
        await Assert.That(healthy.ActualSha256).IsEqualTo(healthy.ExpectedSha256);
    }

    private static Task<IComparisonTarget> OpenTargetAsync(DatabaseFlowFixture fixture, string name, CancellationToken token)
        => DocumentTargetAcquisition.OpenAsync(fixture, name == SurrealTarget, token);
}
