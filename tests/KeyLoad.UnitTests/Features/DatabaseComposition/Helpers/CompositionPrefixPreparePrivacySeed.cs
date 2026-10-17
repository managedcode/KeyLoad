using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.ClusterRouting;
using R = KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

/// <summary>Applies the same logical originals through each independent control-owner journal before retirement.</summary>
internal static class CompositionPrefixPreparePrivacySeed
{
    internal static async Task SubmitAsync<T>(IReadOnlyList<ControlledPartitionMovementNode> sources,
        ReplicatedOperation original, T expected, CancellationToken token)
    {
        await Assert.That(sources.Count).IsEqualTo(R.Voters);
        var source = sources[R.SourceIndex];
        var expectedIndex = source.Journal.Log.State.LastIndex + R.One;
        var bytes = NativeSerialization.Serialize(original);
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(original.NativePayload.Span);
        var fingerprint = NativeOperationFingerprint.Compute(original);
        foreach (var owner in sources)
        {
            await Assert.That(owner.PhysicalOwner).IsEqualTo(source.PhysicalOwner);
            await Assert.That(owner.Journal.Log.State.LastIndex + R.One).IsEqualTo(expectedIndex);
            var issued = ReferenceEquals(owner, source) ? original
                : owner.Database.CreateNativeOperation(original.Kind, original.Id, original.PrincipalId,
                    original.EvaluatedAt, payload.Value);
            await Assert.That(issued.EvaluatedAt).IsEqualTo(original.EvaluatedAt);
            await Assert.That(NativeOperationFingerprint.Compute(issued)).IsEqualTo(fingerprint);
            await Assert.That(NativeSerialization.Deserialize<NativeCommandPayload>(issued.NativePayload.Span).Value.Span
                .SequenceEqual(payload.Value.Span)).IsTrue();
            var issuedBytes = NativeSerialization.Serialize(issued);
            var result = owner.Journal.Submit(issued, token);
            await Assert.That(result.Error).IsNull();
            var actual = result.Get<T>();
            await CompositionPrefixPrepareAssertions.EqualAsync(expected, actual);
            if (expected is CommitReceipt expectedReceipt && actual is CommitReceipt receipt)
            {
                await Assert.That(receipt.Mutations.SelectMany(mutation => mutation.CompositionReferences)
                    .SequenceEqual(expectedReceipt.Mutations.SelectMany(mutation => mutation.CompositionReferences))).IsTrue();
            }
            await Assert.That(owner.Journal.Log.State.LastIndex).IsEqualTo(expectedIndex);
            var retained = owner.Journal.Log.Read(expectedIndex, R.One,
                owner.Journal.Configuration.MaxAppendBytes).Single().Operation!;
            await Assert.That(NativeSerialization.Serialize(retained).SequenceEqual(issuedBytes)).IsTrue();
            await Assert.That(NativeSerialization.Serialize(original).SequenceEqual(bytes)).IsTrue();
        }
    }
}
