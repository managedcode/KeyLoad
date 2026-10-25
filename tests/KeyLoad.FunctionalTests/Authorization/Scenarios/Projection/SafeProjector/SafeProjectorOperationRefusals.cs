namespace KeyLoad.FunctionalTests.Authorization;

internal static class SafeProjectorOperationRefusals
{
    internal static async Task RoundtripAsync(SafeProjectorOperationDatabase owner,
        SafeProjectorOperationHistory history, long epoch, long revision)
    {
        string? projected = null;
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var actual = owner.Database.GetDocument(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First),
                cancellationToken: owner.CallerToken) ?? throw new InvalidOperationException(MissingProjection);
            await Assert.That(actual.Redacted).IsTrue();
            await Assert.That(actual.Json?.Contains(SafeProjectorOperationProtocol.Canary, StringComparison.Ordinal) is true).IsFalse();
            projected = actual.Json;
        });
        var json = projected ?? throw new InvalidOperationException(MissingProjection);
        foreach (var explicitReplacement in new[] { false, true })
        {
            history.Commands.Add(await SafeProjectorOperationMutations.RefusedAsync(owner,
                SafeProjectorOperationMutations.Replacement(json, revision, explicitReplacement),
                SafeProjectorOperationProtocol.ReplacementDenied, epoch));
        }
    }

    internal static async Task ProtectedPatchAsync(SafeProjectorOperationDatabase owner,
        SafeProjectorOperationHistory history, long epoch, long revision)
    {
        history.Commands.Add(await SafeProjectorOperationMutations.RefusedAsync(owner,
            SafeProjectorOperationMutations.ProtectedPatch(revision), SafeProjectorOperationProtocol.WriteDenied, epoch));
    }

    private const string MissingProjection = "The actual safe projector roundtrip document is absent.";
}
