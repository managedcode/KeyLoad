using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class CurrentGitHubJobSelectionTests
{
    private static readonly string[] AcceptedSelections =
    [
        CurrentGitHubJobSelectionTestProtocol.Image,
        CurrentGitHubJobSelectionTestProtocol.Preflight,
        CurrentGitHubJobSelectionTestProtocol.Control,
        CurrentGitHubJobSelectionTestProtocol.Scaled,
        CurrentGitHubJobSelectionTestProtocol.Vector,
        CurrentGitHubJobSelectionTestProtocol.OpenLoop,
        CurrentGitHubJobSelectionTestProtocol.Proof,
    ];

    private static readonly string[] RejectedSelections =
    [
        CurrentGitHubJobSelectionTestProtocol.UnknownJob,
        CurrentGitHubJobSelectionTestProtocol.MissingCellId,
        CurrentGitHubJobSelectionTestProtocol.MissingEvidenceProfile,
        CurrentGitHubJobSelectionTestProtocol.MissingTarget,
        CurrentGitHubJobSelectionTestProtocol.MissingMatrixKind,
        CurrentGitHubJobSelectionTestProtocol.MismatchedJobName,
        CurrentGitHubJobSelectionTestProtocol.MismatchedCellId,
        CurrentGitHubJobSelectionTestProtocol.MismatchedEvidenceProfile,
        CurrentGitHubJobSelectionTestProtocol.MismatchedRowSelector,
        CurrentGitHubJobSelectionTestProtocol.MismatchedTarget,
        CurrentGitHubJobSelectionTestProtocol.MismatchedMatrixKind,
        CurrentGitHubJobSelectionTestProtocol.UnknownCellId,
        CurrentGitHubJobSelectionTestProtocol.ImageWithCellId,
        CurrentGitHubJobSelectionTestProtocol.ImageWithEvidenceProfile,
        CurrentGitHubJobSelectionTestProtocol.ImageWithScaleProfile,
    ];

    [Test]
    public async Task CanonicalImageAndEveryWorkerFamilySelectWithoutMutatingTheirInputContext()
    {
        var result = await CurrentGitHubJobSelectionNodeProcess.RunAsync(
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEqualTo(string.Empty);

        using var document = JsonDocument.Parse(result.Output);
        var outcomes = document.RootElement.EnumerateArray().ToDictionary(
            item => item.GetProperty(CurrentGitHubJobSelectionTestProtocol.NameField).GetString()!, StringComparer.Ordinal);

        foreach (var name in AcceptedSelections)
        {
            var outcome = outcomes[name];
            await Assert.That(outcome.GetProperty(CurrentGitHubJobSelectionTestProtocol.Accepted).GetBoolean()).IsTrue();
            await Assert.That(outcome.GetProperty(CurrentGitHubJobSelectionTestProtocol.ContextUnchanged).GetBoolean()).IsTrue();
            await Assert.That(outcome.GetProperty(CurrentGitHubJobSelectionTestProtocol.SelectedProfile).GetString())
                .IsEqualTo(outcome.GetProperty(CurrentGitHubJobSelectionTestProtocol.ExpectedProfile).GetString());
        }

        await Assert.That(outcomes[CurrentGitHubJobSelectionTestProtocol.Image]
            .GetProperty(CurrentGitHubJobSelectionTestProtocol.ReusesInputContext).GetBoolean())
            .IsTrue();

        await Assert.That(outcomes[CurrentGitHubJobSelectionTestProtocol.Image]
            .GetProperty(CurrentGitHubJobSelectionTestProtocol.SelectedJobName).GetString())
            .IsEqualTo(CurrentGitHubJobSelectionTestProtocol.ImageJobName);

        foreach (var name in RejectedSelections)
        {
            var outcome = outcomes[name];
            await Assert.That(outcome.GetProperty(CurrentGitHubJobSelectionTestProtocol.Accepted).GetBoolean()).IsFalse();
            await Assert.That(outcome.GetProperty(CurrentGitHubJobSelectionTestProtocol.ContextUnchanged).GetBoolean()).IsTrue();
        }
    }
}
