using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.FunctionalTests.RelationalStorage;

internal static class SqlCreateTableOutcomeBytes
{
    private const string Missing = "The original create outcome is not retained.";
    internal static byte[] Read(ZoneTreeStore store, string principal, Guid id)
        => store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(principal, id))
            ?? throw new InvalidOperationException(Missing));
    internal static async Task RequireAsync(ZoneTreeStore store, SqlCreateTableState state)
    {
        await Assert.That(Read(store, state.Principal.Id, state.Created.CommandId)).IsEquivalentTo(state.OriginalBytes,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Read(store, state.Principal.Id, state.Duplicate.CommandId)).IsEquivalentTo(state.FailedBytes,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
