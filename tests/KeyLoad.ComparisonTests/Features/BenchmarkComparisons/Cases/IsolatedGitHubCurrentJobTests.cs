using System.Globalization;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006/007: read-only authenticated GitHub capture in the actual trusted comparison-images job.</summary>
internal sealed class IsolatedGitHubCurrentJobTests
{
    [Test]
    public async Task AcIso007AuthenticatedCurrentJobMatchesActualSourceAndAppendsOnlyItsIdentity()
    {
        var environmentFile = IsolatedGitHubNativeProtocol.Required(IsolatedGitHubNativeProtocol.EnvironmentFile);
        var before = await File.ReadAllTextAsync(environmentFile, TestContext.Current!.Execution.CancellationToken);
        var result = await IsolatedGitHubNativeProcess.RunAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error).IsEmpty();
        using var jobDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(
            IsolatedGitHubNativeProtocol.JobFile(), TestContext.Current!.Execution.CancellationToken));
        var job = jobDocument.RootElement;
        var id = job.GetProperty(IsolatedGitHubNativeProtocol.Id).GetInt64();
        await Assert.That(id).IsGreaterThan(0);
        await Assert.That(job.GetProperty(IsolatedGitHubNativeProtocol.Name).GetString())
            .IsEqualTo(IsolatedGitHubNativeProtocol.ImageJob);
        await Assert.That(job.GetProperty(IsolatedGitHubNativeProtocol.Source).GetString())
            .IsEqualTo(IsolatedGitHubNativeProtocol.Required(ComparisonImageProtocol.ShaEnvironment));
        await Assert.That(job.GetProperty(IsolatedGitHubNativeProtocol.Run).GetInt64())
            .IsEqualTo(long.Parse(IsolatedGitHubNativeProtocol.Required(ComparisonImageProtocol.RunEnvironment), CultureInfo.InvariantCulture));
        await Assert.That(job.GetProperty(IsolatedGitHubNativeProtocol.Status).GetString())
            .IsEqualTo(IsolatedGitHubNativeProtocol.InProgress);
        await Assert.That(job.GetProperty(IsolatedGitHubNativeProtocol.Workflow).GetString())
            .IsEqualTo(IsolatedGitHubNativeProtocol.WorkflowName);
        var expected = before + IsolatedGitHubNativeProtocol.JobEnvironment + id.ToString(CultureInfo.InvariantCulture) + "\n";
        await Assert.That(await File.ReadAllTextAsync(environmentFile, TestContext.Current!.Execution.CancellationToken)).IsEqualTo(expected);
    }
}
