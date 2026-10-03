using System.Text.Json;
using F = KeyLoad.UnitTests.Features.BenchmarkComparisons.IsolatedAggregateFields;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-007 rejects incomplete and foreign raw inventories before parsing cases.</summary>
internal sealed class IsolatedAggregateInventoryTests
{
    private const string Workers = "workers";
    private const string FirstCell = "keyload-n1-point-read";
    private const string WorkerFile = "worker.json";
    private const string ForeignCell = "foreign-n1-point-read";
    private const string OutsideFile = "outside.json";
    private const string EmptyObject = "{}";
    private const string StagingPattern = ".isolated-aggregate-*";

    [Test]
    public async Task AC_ISO_007_ForeignRawCellCannotJoinCanonicalPlan()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = await IsolatedAggregateCliFixture.CreateAsync(token);
        await fixture.CreateInvalidWorkerFilesAsync(token);
        Directory.CreateDirectory(Path.Combine(fixture.Input, Workers, ForeignCell));
        await RejectedAsync(fixture, token);
    }

    [Test]
    public async Task AC_ISO_007_MissingOrSymlinkRawWorkerCannotJoinCanonicalPlan()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = await IsolatedAggregateCliFixture.CreateAsync(token);
        await fixture.CreateInvalidWorkerFilesAsync(token);
        var worker = Path.Combine(fixture.Input, Workers, FirstCell, WorkerFile);
        File.Delete(worker);
        await RejectedAsync(fixture, token);
        var outside = Path.Combine(fixture.Root, OutsideFile);
        await File.WriteAllTextAsync(outside, EmptyObject, token);
        File.CreateSymbolicLink(worker, outside);
        await RejectedAsync(fixture, token);
    }

    private static async Task RejectedAsync(IsolatedAggregateCliFixture fixture, CancellationToken token)
    {
        var response = await fixture.RunAsync(token);
        await Assert.That(response.ExitCode == 0).IsFalse();
        using var receipt = JsonDocument.Parse(response.Error);
        await Assert.That(receipt.RootElement.GetProperty(F.Error).GetString()).IsEqualTo(F.InputError);
        await Assert.That(Directory.Exists(fixture.Output)).IsFalse();
        await Assert.That(Directory.GetDirectories(fixture.Root, StagingPattern).Length).IsEqualTo(0);
    }
}
