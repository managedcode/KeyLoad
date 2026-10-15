using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class CatalogIdentityFault
{
    internal static async Task RunAsync(TestDatabase db, bool credential, CancellationToken token)
    {
        var key = credential ? KeySpace.ApiKey(CatalogIdentityProtocol.Key) : KeySpace.Principal(CatalogIdentityProtocol.Reader);
        var original = db.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original identity row must exist.");
        var damaged = credential ? NativeSerialization.Serialize(NativeSerialization.Deserialize<ApiKeyRecord>(original)
            with
        {
            Id = "different-key"
        }) : db.Store.Read(view => view.ReadOwnedValue(KeySpace.Principal(CatalogIdentityProtocol.Root)))
            ?? throw new InvalidOperationException("The actual privileged native donor row must exist.");
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Write(db, key, damaged);
            await CatalogIdentityLifetime.ConserveAsync(db.Store, Replace(image, key, original, damaged), cut + 1L);
            await CatalogIdentityAssertions.RefusedAsync(db.Database, db.Store, db.Partition, ErrorCode.Corruption,
                credential ? "The authenticated credential identity is inconsistent." : "The persisted principal identity is inconsistent.",
                !credential, token);
        }, failures);
        // Restore this exact owned row even if refusal, body, or conservation assertions failed.
        ServerFailureObserver.Observe(() => Write(db, key, original), failures);
        await ServerFailureObserver.ObserveAsync(() => CatalogIdentityLifetime.ConserveAsync(db.Store, image, cut + 2L), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Write(TestDatabase db, byte[] key, byte[] value)
        => db.Store.Commit((transaction, _) => { transaction.Put(key, value); return true; });

    private static string[] Replace(string[] image, byte[] key, byte[] original, byte[] damaged)
    {
        var prefix = Convert.ToHexString(key) + ":";
        var row = prefix + Convert.ToHexString(original);
        if (!string.Equals(image.Single(item => item.StartsWith(prefix, StringComparison.Ordinal)), row, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The original identity image must contain the exact captured row.");
        }
        return image.Select(item => string.Equals(item, row, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(damaged) : item).ToArray();
    }
}
