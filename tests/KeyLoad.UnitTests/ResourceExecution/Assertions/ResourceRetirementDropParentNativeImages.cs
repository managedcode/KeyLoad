using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementDropParentNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementDropParentNativeImages
{
    internal static Dictionary<string, string> Read(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], ImageLimit);
        if (page.HasMore)
        {
            throw new InvalidOperationException(ResourceRetirementProtocol.Capacity);
        }

        return page.Records.ToDictionary(row => Convert.ToHexString(row.Key.Span),
            row => Convert.ToHexString(row.Value.Span), StringComparer.Ordinal);
    });

    internal static async Task SameAsync(ZoneTreeStore store, Dictionary<string, string> image, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(Read(store).OrderBy(row => row.Key, StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(image.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray(), CollectionOrdering.Matching);
    }

    internal static async Task CreatedAsync(TestDatabase fixture, ResourceRetirementDropParentNativeState state,
        Dictionary<string, string> before, long position)
    {
        var after = Read(fixture.Store);
        var keys = new[] { ResourceRetirementDropParentStorage.Key(state.Lookup, Root),
            KeyCodec.Encode(IdentitySpace, Root, state.Lookup.CommandId),
            KeySpace.GlobalOutcome(Root, state.Original.Id), KeySpace.ClockBytes };
        foreach (var key in keys)
        {
            var name = Convert.ToHexString(key);
            before[name] = after[name];
        }
        await SameAsync(fixture.Store, before, position + First);
        var pointer = fixture.Store.Read(view => view.GetRecord<ResourceRetirementDropParentLookupV1>(keys[PointerIndex]));
        await ResourceIncarnationAssertions.EqualAsync(state.Lookup, pointer);
        await Assert.That(fixture.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!)))
            .IsEqualTo(state.Original.EvaluatedAt);
        await OutcomeAsync(fixture, state);
    }

    internal static async Task ResultAsync(OperationResult expected, OperationResult actual)
    {
        await ResourceIncarnationAssertions.ResultAsync(expected, actual);
        var expectedParent = expected.Get<ResourceRetirementPhaseResultV1>().DropParent!;
        var actualParent = actual.Get<ResourceRetirementPhaseResultV1>().DropParent!;
        await Assert.That(actualParent.OriginalIntent.NativePayload.ToArray())
            .IsEquivalentTo(expectedParent.OriginalIntent.NativePayload.ToArray(), CollectionOrdering.Matching);
        if (expectedParent.OriginalCompletion is { } completion && actualParent.OriginalCompletion is { } actualCompletion)
        {
            await Assert.That(actualCompletion.NativePayload.ToArray())
                .IsEquivalentTo(completion.NativePayload.ToArray(), CollectionOrdering.Matching);
        }
    }

    private static async Task OutcomeAsync(TestDatabase fixture, ResourceRetirementDropParentNativeState state)
    {
        var outcome = fixture.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.GlobalOutcome(Root, state.Original.Id)))!;
        await Assert.That(outcome.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(state.Original));
        await Assert.That(outcome.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(state.Expected.PolicyEpoch);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Global);
        await Assert.That(outcome.Partition).IsNull();
        await Assert.That(outcome.BlobAuthority).IsNull();
        await Assert.That(outcome.CompositionAuthority).IsNull();
        await Assert.That(outcome.OnlineTextAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferFailureAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferAuthority).IsNull();
        await ResultAsync(state.Result, outcome.Result);
    }
}
