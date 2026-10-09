using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-001/004: cover the document branches in the current source closure without publishing controlled fixtures.</summary>
internal sealed class SiteDocumentContractCoverageTests
{
    private const string CellsField = "cells";
    private const string SelectedField = "selected";
    private const string RejectedField = "rejected";
    private const string SupportedField = "supported";
    private const string ComparableRejectedField = "comparableRejected";
    private const string UnavailableField = "unavailable";
    private const string EnvelopeRejectedField = "envelopeRejected";
    [Test]
    [Arguments("plan")]
    [Arguments("evidence")]
    [Arguments("finalizer")]
    public async Task AcMeth004DocumentDependencyClosureUsesActualProductionParsers(string operation)
    {
        var inputs = SiteTestInputs.Read();
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = inputs.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(inputs.Repository, "tests", "KeyLoad.ComparisonTests", "Features",
            "BenchmarkComparisons", "UnitContracts", "Fixtures", "document-contract-probe.mjs"));
        start.ArgumentList.Add(operation);
        var result = await SiteIsolatedNodeProcess.RunProcessAsync(start, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        if (operation == "plan")
        {
            await Assert.That(root.GetProperty(CellsField).GetInt32()).IsEqualTo(418);
            await Assert.That(root.GetProperty(SelectedField).GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty(RejectedField).EnumerateArray().All(value => value.GetBoolean())).IsTrue();
        }
        else if (operation == "evidence")
        {
            await Assert.That(root.GetProperty(SupportedField).GetInt32()).IsEqualTo(38);
            await Assert.That(root.GetProperty(ComparableRejectedField).GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty(UnavailableField).GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty(EnvelopeRejectedField).GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty(RejectedField).EnumerateArray().All(value => value.GetBoolean())).IsTrue();
        }
        else
        {
            await Assert.That(root.EnumerateObject().All(value => value.Value.GetBoolean())).IsTrue();
        }
    }
}
