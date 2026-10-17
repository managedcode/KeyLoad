namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProtectedRebuildColdSeed
{
    internal const string Collection = "protected-cold-text";
    internal const string Reader = "protected-cold-reader";
    internal const string Field = "/secret";
    internal const string Use = "protected.search";
    internal const string Alpha = "alpha";
    internal const string Beta = "beta";
    internal const string OriginalJson = "{\"secret\":\"привіт світ\",\"other\":\"alpha\"}";
    internal const string EnglishJson = "{\"secret\":\"hello world\",\"other\":\"beta\"}";
    internal const string HealthyJson = "{\"secret\":\"завершено світ\",\"other\":\"gamma\"}";

    internal static NativeTextProtectedRebuildColdOwner.Retained[] Initialize(NativeTextProtectedRebuildColdOwner owner)
    {
        owner.Source.Configure(Collection, ResourceKind.Collection,
            fields: [new(Field, "secret", "protected.read", Use, "protected.write")]);
        Policy(owner, 1L, allowed: true);
        return [Batch(owner, new PutDocument(Collection, Alpha, OriginalJson, Access: new(Reader)),
            new PutDocument(Collection, Beta, EnglishJson, Access: new(Reader)))];
    }

    internal static NativeTextProtectedRebuildColdOwner.Retained Batch(
        NativeTextProtectedRebuildColdOwner owner, params Mutation[] mutations)
    {
        var request = new CommandRequest(Guid.NewGuid(), owner.Source.Partition, [.. mutations]);
        return owner.Send(OperationKind.Batch, request, request.CommandId);
    }

    internal static void Policy(NativeTextProtectedRebuildColdOwner owner, long epoch, bool allowed)
    {
        var principal = new PrincipalRecord(Reader, owner.Source.Partition.TenantId,
            [new("database", Collection, Capability.Query | Capability.DocumentsRead)], allowed ? [Use] : [])
        {
            OwnerId = Reader,
            RestrictRows = true,
            PolicyEpoch = epoch
        };
        var actual = owner.Send(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Result.Get<PrincipalRecord>();
        if (actual.PolicyEpoch != epoch)
        {
            throw new InvalidOperationException("The genuine persisted reader epoch differs.");
        }
    }
}
