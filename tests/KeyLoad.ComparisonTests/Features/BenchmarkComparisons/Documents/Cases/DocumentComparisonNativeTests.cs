using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[NotInParallel("NativeDatabaseFlows")]
internal sealed class DocumentComparisonNativeTests
{
    private const long NoObservedOperations = 0;
    private const long CancellationAcknowledgements = 20;
    private const long MeasuredOperations = 100;
    private const int FirstRecord = 0;
    private const int SingleClient = 1;
    private const int ConcurrentClients = 10;
    private const int OrdinaryClients = 16;
    private const int CancellationThreshold = 20;
    private const int SmallCorpusRecords = 100;
    private const int MaximumIdentityExclusive = 101;
    private const int InitialRecords = 200;
    private const int CreatedFinalRecords = 300;
    private const int CancellationOperations = 1000;
    private const int CanonicalRecords = 100_000;
    private const int CanonicalIngestionRecords = 1_000_000;
    private const string DevelopmentFamily = "document-v1-development";
    private const string MeasuredStatus = "measured";
    private const string FailedStatus = "failed";
    private const string SurrealTarget = "SurrealDB";

    [Test]
    [Arguments("SurrealDB", DocumentComparisonScenario.SequentialRead)]
    [Arguments("SurrealDB", DocumentComparisonScenario.RandomRead)]
    [Arguments("SurrealDB", DocumentComparisonScenario.Create)]
    [Arguments("SurrealDB", DocumentComparisonScenario.Update)]
    [Arguments("SurrealDB", DocumentComparisonScenario.Delete)]
    [Arguments("SurrealDB", DocumentComparisonScenario.ReadUpdate50)]
    [Arguments("SurrealDB", DocumentComparisonScenario.ReadUpdate95)]
    [Arguments("SurrealDB", DocumentComparisonScenario.MixedCrud)]
    [Arguments("SurrealDB", DocumentComparisonScenario.Ingest)]
    [Arguments("HelixDB", DocumentComparisonScenario.SequentialRead)]
    [Arguments("HelixDB", DocumentComparisonScenario.RandomRead)]
    [Arguments("HelixDB", DocumentComparisonScenario.Create)]
    [Arguments("HelixDB", DocumentComparisonScenario.Update)]
    [Arguments("HelixDB", DocumentComparisonScenario.Delete)]
    [Arguments("HelixDB", DocumentComparisonScenario.ReadUpdate50)]
    [Arguments("HelixDB", DocumentComparisonScenario.ReadUpdate95)]
    [Arguments("HelixDB", DocumentComparisonScenario.MixedCrud)]
    [Arguments("HelixDB", DocumentComparisonScenario.Ingest)]
    public async Task RealNativeSharedScheduleVerifiesCompleteFinalState(string name, DocumentComparisonScenario scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await NativeDatabaseFlowFixture.CreateAsync(name, token);
        var clients = scenario == DocumentComparisonScenario.Ingest ? ConcurrentClients : OrdinaryClients;
        var selected = new DocumentComparisonSelection(scenario, scenario == DocumentComparisonScenario.Ingest ? CanonicalIngestionRecords : CanonicalRecords, clients);
        var runner = new DocumentComparisonRunner(NativeDatabaseFlowFixture.ExecutionOptions);
        var report = await runner.RunDevelopmentAsync((_, cancellation) => OpenTargetAsync(fixture, name, cancellation), selected,
            records: InitialRecords, operations: SmallCorpusRecords, token);
        var repetition = report.Repetitions.Single();
        await Assert.That(report.Family).IsEqualTo(DevelopmentFamily);
        await Assert.That(report.Qualified).IsFalse();
        await Assert.That(repetition.Status).IsEqualTo(MeasuredStatus);
        await Assert.That(repetition.Errors).IsEmpty();
        await Assert.That(repetition.OpenedClients).IsEqualTo(clients);
        await Assert.That(repetition.Attempts).IsEqualTo(MeasuredOperations);
        await Assert.That(repetition.Acknowledged).IsEqualTo(MeasuredOperations);
        await Assert.That(repetition.Failed + repetition.Canceled + repetition.Unfinished).IsEqualTo(NoObservedOperations);
        await Assert.That(repetition.Histogram.Count).IsEqualTo(MeasuredOperations);
        await Assert.That(repetition.Histogram.BucketCounts.Sum()).IsEqualTo(MeasuredOperations);
        await Assert.That(repetition.Histogram.OverflowCount).IsEqualTo(NoObservedOperations);
        await Assert.That(repetition.ActualFinalRecords).IsEqualTo(scenario switch
        {
            DocumentComparisonScenario.Ingest => SmallCorpusRecords,
            DocumentComparisonScenario.Create => CreatedFinalRecords,
            DocumentComparisonScenario.Delete => SmallCorpusRecords,
            _ => InitialRecords
        });
        await Assert.That(repetition.ActualSha256).IsEqualTo(repetition.ExpectedSha256);
    }

    [Test]
    [Arguments("SurrealDB")]
    [Arguments("HelixDB")]
    public async Task CancellationAfterActualAcknowledgementsSettlesClientsThenHealthyNativeFollowup(string name)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await NativeDatabaseFlowFixture.CreateAsync(name, token);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var observed = NoObservedOperations;
        var runner = new DocumentComparisonRunner(NativeDatabaseFlowFixture.ExecutionOptions, nativeProgress: progress =>
        {
            Interlocked.Exchange(ref observed, progress.Acknowledged);
            if (progress.Acknowledged >= CancellationThreshold)
            {
                canceled.Cancel();
            }
        });
        var selection = new DocumentComparisonSelection(DocumentComparisonScenario.Ingest, CanonicalIngestionRecords, ConcurrentClients);
        var failed = await runner.RunDevelopmentAsync((_, cancellation) => OpenTargetAsync(fixture, name, cancellation),
            selection, records: CancellationOperations, operations: CancellationOperations, canceled.Token);
        await Assert.That(observed).IsGreaterThanOrEqualTo(CancellationAcknowledgements);
        await Assert.That(failed.Status).IsEqualTo(FailedStatus);
        await Assert.That(failed.Qualified).IsFalse();
        var actual = failed.Repetitions.Single();
        await Assert.That(actual.Attempts).IsEqualTo(actual.Acknowledged + actual.Failed + actual.Canceled);
        await Assert.That(actual.Histogram.Count).IsEqualTo(actual.Attempts);
        var healthy = await new DocumentComparisonRunner(NativeDatabaseFlowFixture.ExecutionOptions)
            .RunDevelopmentAsync((_, cancellation) => OpenTargetAsync(fixture, name, cancellation), selection,
                records: InitialRecords, operations: SmallCorpusRecords, token);
        await Assert.That(healthy.Repetitions.Single().Status).IsEqualTo(MeasuredStatus);
        await Assert.That(healthy.Repetitions.Single().ActualFinalRecords).IsEqualTo(SmallCorpusRecords);
    }

    [Test]
    [Arguments("SurrealDB")]
    [Arguments("HelixDB")]
    public async Task RealNativeExtraMissingAndChangedBodiesRejectBeforeHealthyCorrectedReadback(string name)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await NativeDatabaseFlowFixture.CreateAsync(name, token);
        await using var target = await OpenTargetAsync(fixture, name, token);
        var native = (IDocumentComparisonTarget)target;
        var corpus = new DocumentComparisonCorpus(SmallCorpusRecords, SingleClient);
        await native.InitializeDocumentsAsync(new(corpus, true, MaximumIdentityExclusive), token);
        await using var session = await native.OpenDocumentSessionAsync(token);
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

    private static Task<IComparisonTarget> OpenTargetAsync(NativeDatabaseFlowFixture fixture, string name, CancellationToken token)
        => DocumentNativeTargetAcquisition.OpenAsync(fixture, name == SurrealTarget, token);
}
