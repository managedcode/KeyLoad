using System.Text.Json;
using KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.ClusterRouting;

internal sealed class RequestCqrsImageJournalTests
{
    private const string FortyOneSequence = "sequence41";
    private const string BoundarySequence = "boundary64";
    private const string OutputOverflow = "output-overflow";
    private const string NodeArgumentsMode = "--input-type=module";
    private const string NodeArgumentsEval = "-e";
    private const string ScriptsDirectory = "scripts";
    private const string FeaturesDirectory = "Features";
    private const string BenchmarkComparisonsDirectory = "BenchmarkComparisons";
    private const string ProcessModule = "image-process.mjs";
    private const string EvidenceModule = "image-evidence.mjs";
    private const string ContractModule = "image-contracts.mjs";
    private const int CompleteSequenceRecords = 41;
    private const int MaximumJournalRecords = 64;
    private const string CountProperty = "count";
    private const string RejectedProperty = "rejected65";
    private const string UnchangedProperty = "journalUnchanged";
    private const string OverflowProperty = "overflowRecorded";
    private const string ExitedProperty = "childSettled";
    private const string OrderedProperty = "ordered";
    private const string SuccessesProperty = "actualSuccesses";
    private const string Command65ExitCodeProperty = "command65ExitCode";
    private const string FailureMatchesProperty = "failureRecordMatchesChild";
    private const string CapturedBytesProperty = "capturedBytes";

    [Test]
    public async Task AcCrs003RecordsTheCompleteFortyOneCommandSequence()
    {
        using var report = await RunAsync(FortyOneSequence);
        await Assert.That(report.RootElement.GetProperty(CountProperty).GetInt32()).IsEqualTo(CompleteSequenceRecords);
        await Assert.That(report.RootElement.GetProperty(OrderedProperty).GetBoolean()).IsTrue();
        await Assert.That(report.RootElement.GetProperty(SuccessesProperty).GetInt32()).IsEqualTo(CompleteSequenceRecords);
    }

    [Test]
    public async Task AcCrs003AcceptsExactlySixtyFourAndRejectsCommandSixtyFiveWithoutMutation()
    {
        using var report = await RunAsync(BoundarySequence);
        await Assert.That(report.RootElement.GetProperty(CountProperty).GetInt32()).IsEqualTo(MaximumJournalRecords);
        await Assert.That(report.RootElement.GetProperty(RejectedProperty).GetBoolean()).IsTrue();
        await Assert.That(report.RootElement.GetProperty(UnchangedProperty).GetBoolean()).IsTrue();
        await Assert.That(report.RootElement.GetProperty(Command65ExitCodeProperty).GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task AcCrs003RecordsJoinedNativeOutputOverflowFailure()
    {
        using var report = await RunAsync(OutputOverflow);
        await Assert.That(report.RootElement.GetProperty(OverflowProperty).GetBoolean()).IsTrue();
        await Assert.That(report.RootElement.GetProperty(ExitedProperty).GetBoolean()).IsTrue();
        await Assert.That(report.RootElement.GetProperty(FailureMatchesProperty).GetBoolean()).IsTrue();
        await Assert.That(report.RootElement.GetProperty(CapturedBytesProperty).GetInt32()).IsEqualTo(262144);
    }

    private static async Task<JsonDocument> RunAsync(string mode)
    {
        var result = await SessionNodeProcess.RunAsync(
            [NodeArgumentsMode, NodeArgumentsEval, RequestCqrsImageJournalNodeProgram.Source,
                ProcessModulePath(), EvidenceModulePath(), ContractModulePath(), mode],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
        return JsonDocument.Parse(result.Output);
    }

    private static string ProcessModulePath()
        => Path.Combine(SessionNodeProcess.RepositoryRoot(), ScriptsDirectory, FeaturesDirectory, BenchmarkComparisonsDirectory, ProcessModule);

    private static string EvidenceModulePath()
        => Path.Combine(SessionNodeProcess.RepositoryRoot(), ScriptsDirectory, FeaturesDirectory, BenchmarkComparisonsDirectory, EvidenceModule);

    private static string ContractModulePath()
        => Path.Combine(SessionNodeProcess.RepositoryRoot(), ScriptsDirectory, FeaturesDirectory, BenchmarkComparisonsDirectory, ContractModule);
}
