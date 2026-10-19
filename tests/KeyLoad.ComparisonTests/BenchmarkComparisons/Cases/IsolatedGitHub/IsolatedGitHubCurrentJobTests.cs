using System.Globalization;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006/007: read-only authenticated GitHub capture in the actual trusted execution preparation job.</summary>
internal sealed class IsolatedGitHubCurrentJobTests
{
    [Test]
    public async Task AcIso007AuthenticatedCurrentJobMatchesActualSourceAndAppendsOnlyItsIdentity()
    {
        var environmentFile = IsolatedGitHubProtocol.Required(IsolatedGitHubProtocol.EnvironmentFile);
        var before = await File.ReadAllTextAsync(environmentFile, TestContext.Current!.Execution.CancellationToken);
        var result = await IsolatedGitHubProcess.RunAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error).IsEmpty();
        using var jobDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(
            IsolatedGitHubProtocol.JobFile(), TestContext.Current!.Execution.CancellationToken));
        var job = jobDocument.RootElement;
        var id = job.GetProperty(IsolatedGitHubProtocol.Id).GetInt64();
        await Assert.That(id).IsGreaterThan(0);
        await Assert.That(job.GetProperty(IsolatedGitHubProtocol.Name).GetString())
            .IsEqualTo(IsolatedGitHubProtocol.PreparationJob);
        await Assert.That(job.GetProperty(IsolatedGitHubProtocol.Source).GetString())
            .IsEqualTo(IsolatedGitHubProtocol.Required(IsolatedGitHubProtocol.ShaEnvironment));
        await Assert.That(job.GetProperty(IsolatedGitHubProtocol.Run).GetInt64())
            .IsEqualTo(long.Parse(IsolatedGitHubProtocol.Required(IsolatedGitHubProtocol.RunEnvironment), CultureInfo.InvariantCulture));
        await Assert.That(job.GetProperty(IsolatedGitHubProtocol.Status).GetString())
            .IsEqualTo(IsolatedGitHubProtocol.InProgress);
        await Assert.That(job.GetProperty(IsolatedGitHubProtocol.Workflow).GetString())
            .IsEqualTo(IsolatedGitHubProtocol.WorkflowName);
        var expected = before + IsolatedGitHubProtocol.JobEnvironment + id.ToString(CultureInfo.InvariantCulture) + "\n";
        await Assert.That(await File.ReadAllTextAsync(environmentFile, TestContext.Current!.Execution.CancellationToken)).IsEqualTo(expected);
    }
}
