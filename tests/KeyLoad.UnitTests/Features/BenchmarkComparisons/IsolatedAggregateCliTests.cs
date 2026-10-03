using System.Text.Json;
using F = KeyLoad.UnitTests.Features.BenchmarkComparisons.IsolatedAggregateFields;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-007 real CLI rejection and publication-preservation tests.</summary>
internal sealed class IsolatedAggregateCliTests
{
    private const string Cells = "cells";
    private const string Job = "job";
    private const string Id = "id";
    private const string Conclusion = "conclusion";
    private const string Failure = "failure";
    private const string SentinelFile = "prior-publication.json";
    private const string SentinelContent = "preserve original publication bytes";
    private const string LinkName = "linked-input";
    private const string TraversalSuffix = "/../input";
    private const string TraversalOutput = "traversal-output";
    private const string LinkOutput = "link-output";
    private const string StagingPattern = ".isolated-aggregate-*";

    [Test]
    public async Task AC_ISO_007_MissingWorkersLeaveNoOutput()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = await IsolatedAggregateCliFixture.CreateAsync(token);
        var response = await fixture.RunAsync(token);
        await AssertErrorAsync(response, F.InputError);
        await Assert.That(Directory.Exists(fixture.Output)).IsFalse();
    }

    [Test]
    public async Task AC_ISO_007_MissingDuplicateAndFailedProofCellsCannotCreateOutput()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = await IsolatedAggregateCliFixture.CreateAsync(token);
        var proof = await fixture.ReadProofAsync(token);
        var cells = proof[Cells]!.AsArray();
        cells[1]![Job]![Id] = cells[0]![Job]![Id]!.GetValue<long>();
        await fixture.WriteProofAsync(proof, token);
        await AssertRejectedAsync(fixture, F.ProofError, token);
        cells[1]![Job]![Id] = 1001;
        cells[0]![Job]![Conclusion] = Failure;
        await fixture.WriteProofAsync(proof, token);
        await AssertRejectedAsync(fixture, F.ProofError, token);
        cells.RemoveAt(0);
        await fixture.WriteProofAsync(proof, token);
        await AssertRejectedAsync(fixture, F.ProofError, token);
    }

    [Test]
    public async Task AC_ISO_007_UnsafePathsAndSymlinksLeaveExistingPublicationByteIdentical()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = await IsolatedAggregateCliFixture.CreateAsync(token);
        Directory.CreateDirectory(fixture.Output);
        var sentinel = Path.Combine(fixture.Output, SentinelFile);
        await File.WriteAllTextAsync(sentinel, SentinelContent, token);
        var response = await fixture.RunAsync(token);
        await AssertErrorAsync(response, F.OutputError);
        await Assert.That(await File.ReadAllTextAsync(sentinel, token)).IsEqualTo(SentinelContent);
        var traversalOutput = Path.Combine(fixture.Root, TraversalOutput);
        var traversal = await fixture.RunAsync(token, input: fixture.Input + TraversalSuffix, output: traversalOutput);
        await AssertErrorAsync(traversal, F.InputError);
        await Assert.That(Directory.Exists(traversalOutput)).IsFalse();
        var link = Path.Combine(fixture.Root, LinkName);
        Directory.CreateSymbolicLink(link, fixture.Input);
        var linkOutput = Path.Combine(fixture.Root, LinkOutput);
        var symlink = await fixture.RunAsync(token, input: link, output: linkOutput);
        await AssertErrorAsync(symlink, F.InputError);
        await Assert.That(Directory.Exists(linkOutput)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(sentinel, token)).IsEqualTo(SentinelContent);
    }

    [Test]
    public async Task AC_ISO_007_SuppliedWorkerHashMismatchCannotCreatePublication()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = await IsolatedAggregateCliFixture.CreateAsync(token);
        await fixture.CreateInvalidWorkerFilesAsync(token);
        await AssertRejectedAsync(fixture, F.ProofError, token);
        await Assert.That(Directory.GetDirectories(fixture.Root, StagingPattern).Length).IsEqualTo(0);
    }

    private static async Task AssertRejectedAsync(IsolatedAggregateCliFixture fixture, string error, CancellationToken token)
    {
        await AssertErrorAsync(await fixture.RunAsync(token), error);
        await Assert.That(Directory.Exists(fixture.Output)).IsFalse();
    }

    private static async Task AssertErrorAsync(IsolatedAggregateNodeResult response, string error)
    {
        await Assert.That(response.ExitCode == 0).IsFalse();
        await Assert.That(response.Output).IsEqualTo(string.Empty);
        using var receipt = JsonDocument.Parse(response.Error);
        await Assert.That(receipt.RootElement.GetProperty(F.Error).GetString()).IsEqualTo(error);
    }
}
