using System.Text.Json.Nodes;
using F = KeyLoad.UnitTests.Features.BenchmarkComparisons.IsolatedAggregateFields;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006/007 real Node validation over complete bounded contract inputs.</summary>
internal sealed class IsolatedAggregateEnvelopeTests
{
    [Test]
    public async Task AC_ISO_006_AcceptsAllFiveTenThousandSampleRepetitionsWithoutFlatteningHost()
    {
        var value = IsolatedAggregateData.Envelope();
        await Assert.That(await AcceptedAsync(value)).IsTrue();
        await Assert.That(value[F.Report]![F.Cases]!.AsArray().Count).IsEqualTo(5);
        await Assert.That(value[F.Report]![F.HostOs]!.GetValue<string>()).IsEqualTo(F.Ubuntu);
    }

    [Test]
    public async Task AC_ISO_007_RejectsMixedIdentityForeignFieldsAndNonLinuxHosts()
    {
        var value = IsolatedAggregateData.Envelope();
        value[F.Worker]![F.SourceRevision] = IsolatedAggregateData.OtherRevision;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        value[F.Worker]![F.SourceRevision] = IsolatedAggregateData.SourceRevision;
        value[F.Worker]![F.Attempt] = 2;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        value[F.Worker]![F.Attempt] = IsolatedAggregateData.Attempt;
        value[F.Report]![F.HostOs] = F.Windows;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        value[F.Report]![F.HostOs] = F.Ubuntu;
        value[F.Foreign] = true;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
    }

    [Test]
    public async Task AC_ISO_007_RejectsNativeCountOptionsMissingSamplesDuplicateCasesAndWrongMetrics()
    {
        var value = IsolatedAggregateData.Envelope();
        var report = value[F.Report]!.AsObject();
        report[F.Targets]![0]![F.Cluster]![F.Nodes] = 1;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        report[F.Targets]![0]![F.Cluster]![F.Nodes] = 2;
        report[F.Options]![F.Concurrency] = 15;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        report[F.Options]![F.Concurrency] = 16;
        var first = report[F.Cases]![0]!.AsObject();
        first[F.Measurement]![F.Throughput] = 999;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        first[F.Measurement]![F.Throughput] = 1000;
        report[F.Cases]![1]![F.Repetition] = 0;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        report[F.Cases]![1]![F.Repetition] = 1;
        first[F.Samples]!.AsArray().RemoveAt(0);
        await Assert.That(await AcceptedAsync(value)).IsFalse();
    }

    [Test]
    public async Task AC_ISO_007_FailedAttemptsCannotBeRelabelledUnsupported()
    {
        var value = IsolatedAggregateData.Envelope();
        value[F.Report]![F.Cases]![0]![F.Samples]![0]![F.Success] = false;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
        value[F.Disposition] = F.UnsupportedTopology;
        value[F.Reason] = F.StartupFailure;
        value[F.Report] = null;
        await Assert.That(await AcceptedAsync(value)).IsFalse();
    }

    private static Task<bool> AcceptedAsync(JsonObject value)
        => IsolatedAggregateProbe.AcceptedAsync(value, IsolatedAggregateData.Cell(), TestContext.Current!.Execution.CancellationToken);
}
