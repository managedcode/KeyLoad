using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteUnsupportedProducerFileChange(string Path, Action<JsonNode> Apply);

internal static class SiteUnsupportedProducerAssertions
{
    internal static async Task RejectThenRestoreAndProveCurrentAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate rejected, SiteUnsupportedProducerCandidate current,
        IReadOnlyList<SiteUnsupportedProducerFileChange> changes, CancellationToken token)
    {
        var originals = new byte[changes.Count][];
        for (var index = 0; index < changes.Count; index++)
        {
            originals[index] = await File.ReadAllBytesAsync(changes[index].Path, token);
        }

        var rejectedInputs = new byte[changes.Count][];
        var failures = new List<Exception>();
        await ObserveFailureAsync(() => RejectAndVerifyAsync(scope, rejected, changes, originals, rejectedInputs, token), failures);
        await RestoreInputsAsync(changes, originals, failures);
        ThrowFailures(failures);
        await AssertFilesEqualAsync(changes, originals, token);
        await AssertCurrentSelectedAsync(scope, current, token);
    }

    internal static async Task AssertCurrentSelectedAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate current, CancellationToken token)
    {
        var response = await SiteUnsupportedProducerControls.SelectAsync(scope, current, token);
        await Assert.That(response.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var result = response.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.State).GetString())
            .IsEqualTo(SiteIsolatedGitHubFields.Selected);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubSelectionFields.RunId).GetInt64())
            .IsEqualTo(current.RunId);
    }

    private static async Task ApplyChangesAsync(IReadOnlyList<SiteUnsupportedProducerFileChange> changes,
        byte[][] originals, byte[][] rejectedInputs, CancellationToken token)
    {
        for (var index = 0; index < changes.Count; index++)
        {
            var node = JsonNode.Parse(originals[index])!;
            changes[index].Apply(node);
            rejectedInputs[index] = Encoding.UTF8.GetBytes(node.ToJsonString());
            await File.WriteAllBytesAsync(changes[index].Path, rejectedInputs[index], token);
        }
    }

    private static async Task AssertFilesEqualAsync(IReadOnlyList<SiteUnsupportedProducerFileChange> changes,
        byte[][] expected, CancellationToken token)
    {
        for (var index = 0; index < changes.Count; index++)
        {
            var actual = await File.ReadAllBytesAsync(changes[index].Path, token);
            await Assert.That(actual.SequenceEqual(expected[index])).IsTrue();
        }
    }

    private static async Task RejectAndVerifyAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate rejected, IReadOnlyList<SiteUnsupportedProducerFileChange> changes,
        byte[][] originals, byte[][] rejectedInputs, CancellationToken token)
    {
        await ApplyChangesAsync(changes, originals, rejectedInputs, token);
        var response = await SiteUnsupportedProducerControls.SelectAsync(scope, rejected, token);
        await Assert.That(response.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        await AssertFilesEqualAsync(changes, rejectedInputs, token);
    }

    private static async Task RestoreInputsAsync(IReadOnlyList<SiteUnsupportedProducerFileChange> changes,
        byte[][] originals, List<Exception> failures)
    {
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            var original = originals[index];
            await ObserveFailureAsync(() => File.WriteAllBytesAsync(change.Path, original, CancellationToken.None), failures);
        }
    }

    private static async Task ObserveFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        var original = InvokeAsync(operation);
        await original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (original.IsFaulted)
        {
            failures.AddRange(original.Exception!.InnerExceptions);
        }
        else if (original.IsCanceled)
        {
            try
            {
                await original;
            }
            catch (OperationCanceledException exception)
            {
                failures.Add(exception);
            }
        }
    }

    private static async Task InvokeAsync(Func<Task> operation) => await operation();

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        throw new AggregateException("Selection and/or input restoration failed.", failures);
    }
}
