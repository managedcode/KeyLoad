using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class CompositeSparseUniqueTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Kl011CompositeNullAndMissingUniquePoliciesPreserveLiteralNativeDeltas(bool includeNull, bool includeMissing)
    {
        using var db = new TestDatabase();
        db.Configure(CompositeSparseUniqueAssertions.Collection, ResourceKind.Collection,
            indexes: [new(CompositeSparseUniqueAssertions.Index, ["/label", "/rank"], Unique: true,
                IncludeNull: includeNull, IncludeMissing: includeMissing)]);
        DocumentCrudFixture.ConfigurePrincipal(db, CompositeSparseUniqueAssertions.Principal,
            CompositeSparseUniqueAssertions.Collection, Capability.DocumentsRead | Capability.DocumentsWrite, []);
        CompositeSparseUniqueAssertions.Submit(db,
            new PutDocument(CompositeSparseUniqueAssertions.Collection, CompositeSparseUniqueAssertions.NullId, CompositeSparseUniqueAssertions.NullJson, 0),
            new PutDocument(CompositeSparseUniqueAssertions.Collection, CompositeSparseUniqueAssertions.MissingId, CompositeSparseUniqueAssertions.MissingJson, 0)).Get<CommitReceipt>();
        await CompositeSparseUniqueAssertions.DocumentAsync(db, CompositeSparseUniqueAssertions.NullId, CompositeSparseUniqueAssertions.NullJson, 1);
        await CompositeSparseUniqueAssertions.DocumentAsync(db, CompositeSparseUniqueAssertions.MissingId, CompositeSparseUniqueAssertions.MissingJson, 1);
        var included = new List<(string Id, object? Rank)>();
        if (includeNull)
        { included.Add((CompositeSparseUniqueAssertions.NullId, null)); }
        if (includeMissing)
        { included.Add((CompositeSparseUniqueAssertions.MissingId, MissingValue.Instance)); }
        await CompositeSparseUniqueAssertions.ImagesAsync(db, [.. included]);
        await CompositeSparseUniqueAssertions.DuplicateAsync(db, CompositeSparseUniqueAssertions.NullDuplicate,
            CompositeSparseUniqueAssertions.NullJson, includeNull);
        await CompositeSparseUniqueAssertions.DuplicateAsync(db, CompositeSparseUniqueAssertions.MissingDuplicate,
            CompositeSparseUniqueAssertions.MissingJson, includeMissing);
        await CompositeSparseUniqueAssertions.ImagesAsync(db, [.. included]);
        CompositeSparseUniqueAssertions.Submit(db,
            new PutDocument(CompositeSparseUniqueAssertions.Collection, CompositeSparseUniqueAssertions.NullId, CompositeSparseUniqueAssertions.SevenJson, 1, ExplicitReplacement: true),
            new PutDocument(CompositeSparseUniqueAssertions.Collection, CompositeSparseUniqueAssertions.MissingId, CompositeSparseUniqueAssertions.EightJson, 1, ExplicitReplacement: true)).Get<CommitReceipt>();
        await CompositeSparseUniqueAssertions.ImagesAsync(db,
            [(CompositeSparseUniqueAssertions.NullId, 7m), (CompositeSparseUniqueAssertions.MissingId, 8m)]);
        await CompositeSparseUniqueAssertions.DocumentAsync(db, CompositeSparseUniqueAssertions.NullId, CompositeSparseUniqueAssertions.SevenJson, 2);
        await CompositeSparseUniqueAssertions.DocumentAsync(db, CompositeSparseUniqueAssertions.MissingId, CompositeSparseUniqueAssertions.EightJson, 2);
        CompositeSparseUniqueAssertions.Submit(db,
            new DeleteDocument(CompositeSparseUniqueAssertions.Collection, CompositeSparseUniqueAssertions.NullId, 2),
            new PutDocument(CompositeSparseUniqueAssertions.Collection, CompositeSparseUniqueAssertions.HealthyId, CompositeSparseUniqueAssertions.SevenJson, 0)).Get<CommitReceipt>();
        await CompositeSparseUniqueAssertions.ImagesAsync(db,
            [(CompositeSparseUniqueAssertions.HealthyId, 7m), (CompositeSparseUniqueAssertions.MissingId, 8m)]);
        await CompositeSparseUniqueAssertions.DocumentAsync(db, CompositeSparseUniqueAssertions.NullId, null, 0);
        await CompositeSparseUniqueAssertions.DocumentAsync(db, CompositeSparseUniqueAssertions.HealthyId, CompositeSparseUniqueAssertions.SevenJson, 1);
        await CompositeSparseUniqueAssertions.DocumentAsync(db, CompositeSparseUniqueAssertions.MissingId, CompositeSparseUniqueAssertions.EightJson, 2);
    }
}
