using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementDropParentNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementDropParentNativeRefusals
{
    internal static async Task EarlyCompletionAsync(TestDatabase fixture, ResourceRetirementDropParentNativeState state,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = ResourceRetirementDropParentNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var id = ResourceRetirementDropParentIdentity.CommandId(state.Lookup, ResourceRetirementPhase.CompleteDropParent);
        var phase = new ResourceRetirementPhaseCommandV1(Version, state.Lookup.CommandId,
            ResourceRetirementPhase.CompleteDropParent, NativeSerialization.Serialize(new ResourceRetirementCompleteDropParentBodyV1(state.Lookup)));
        var original = state.Database.CreateResourceRetirementOperation(id, Root, state.Database.EvaluationClock.GetUtcNow(), phase);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => state.Database.Apply(original));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await ResourceRetirementDropParentNativeImages.SameAsync(fixture.Store, image, position);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(Root, id)))).IsNull();
    }

    internal static async Task MissingOutcomeAsync(TestDatabase fixture, ResourceRetirementDropParentNativeState state,
        CancellationToken token)
    {
        var key = KeySpace.GlobalOutcome(Root, state.Original.Id);
        fixture.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var image = ResourceRetirementDropParentNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var read = Assert.ThrowsExactly<KeyLoadException>(() => state.Database.ReadResourceRetirementDropParent(
            Root, state.Lookup, fixture.CreateReadWork(token)));
        await Assert.That(read!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await ResourceRetirementDropParentNativeImages.SameAsync(fixture.Store, image, position);
        var apply = Assert.ThrowsExactly<KeyLoadException>(() => state.Database.Apply(state.Original));
        await Assert.That(apply!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await ResourceRetirementDropParentNativeImages.SameAsync(fixture.Store, image, position);
        fixture.Store.Commit((transaction, _) => { transaction.Put(key, state.OutcomeBytes); return true; });
        await ResourceRetirementDropParentNativeFlow.RetainedAsync(fixture, state.Database, state, token);
    }
}
