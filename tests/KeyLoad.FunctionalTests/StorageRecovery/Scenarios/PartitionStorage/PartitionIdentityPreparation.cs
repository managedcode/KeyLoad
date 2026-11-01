namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionIdentityPreparation
{
    internal static async Task<PartitionIdentityCut> SeedAsync(PartitionHostRecoveryFixture fixture)
    {
        await fixture.RunHostAsync(host =>
        {
            host.Database.Store.Commit((transaction, _) =>
            {
                transaction.Put(PartitionIdentityProtocol.OriginalKey, PartitionIdentityProtocol.OriginalValue);
                return true;
            });
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        PartitionIdentityCut cut = null!;
        await fixture.RunStoresAsync(stores =>
        {
            cut = new(stores.Canonical.Identity, stores.Canonical.Position);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return cut;
    }
}
