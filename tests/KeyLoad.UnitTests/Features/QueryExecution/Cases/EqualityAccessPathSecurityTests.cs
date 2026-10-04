using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class EqualityAccessPathSecurityTests
{
    private const string SensitiveLabel = "pii";
    private const string Owner = "alice";
    private const string OtherOwner = "bob";
    private const int MinimumReadBudgetBytes = 1;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql008ProtectedReversedEqualityIsDeniedBeforeTheIndexReadBudget(bool parameterized)
    {
        using var db = new TestDatabase(new() { MaxQueryReadBytes = MinimumReadBudgetBytes });
        db.Configure(EqualityAccessPathSupport.Collection, ResourceKind.Collection,
            indexes: [new(EqualityAccessPathSupport.StatusIndex, [EqualityAccessPathSupport.StatusPath])],
            fields: [new(EqualityAccessPathSupport.StatusPath, SensitiveLabel)]);
        ConfigureReader(db, EqualityAccessPathSupport.Reader);
        var engine = new QueryEngine(db.Database);
        foreach (var reversed in new[] { false, true })
        {
            var request = EqualityAccessPathSupport.Request(db, indexed: true, parameterized, reversed);
            var error = EqualityAccessPathSupport.Failure(engine, request, EqualityAccessPathSupport.Reader);
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AcAisql008ReversedPointAndIndexPredicatesPreservePersistedRowOwnership(bool indexed, bool parameterized)
    {
        using var db = new TestDatabase();
        db.Configure(EqualityAccessPathSupport.Collection, ResourceKind.Collection,
            indexes: [new(EqualityAccessPathSupport.StatusIndex, [EqualityAccessPathSupport.StatusPath])]);
        db.Commit(new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.PrimaryId,
            EqualityAccessPathSupport.PrimaryJson, Access: new(OtherOwner)),
            new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.SecondaryId,
                EqualityAccessPathSupport.SecondaryJson, Access: new(Owner)));
        ConfigureReader(db, Owner, restrictRows: true);
        var engine = new QueryEngine(db.Database);
        var ordinary = engine.Execute(Owner, EqualityAccessPathSupport.Request(db, indexed, parameterized, reversed: false));
        var reversed = engine.Execute(Owner, EqualityAccessPathSupport.Request(db, indexed, parameterized, reversed: true));

        await EqualityAccessPathSupport.SameRows(ordinary, reversed);
        if (indexed)
        {
            await Assert.That(reversed.Rows).HasSingleItem();
            await Assert.That(reversed.Rows[0].EntityId).IsEqualTo(EqualityAccessPathSupport.SecondaryId);
        }
        else
        {
            await Assert.That(reversed.Rows).IsEmpty();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql008MissingReversedParameterIsRejectedBeforeCandidateSelection(bool indexed)
    {
        using var db = EqualityAccessPathSupport.Create(new() { MaxQueryReadBytes = MinimumReadBudgetBytes });
        var engine = new QueryEngine(db.Database);
        foreach (var reversed in new[] { false, true })
        {
            var request = EqualityAccessPathSupport.Request(db, indexed, parameterized: true, reversed) with { Parameters = null };
            await Assert.That(EqualityAccessPathSupport.Failure(engine, request).Code).IsEqualTo(ErrorCode.Validation);
        }
    }

    private static void ConfigureReader(TestDatabase db, string principal, bool restrictRows = false)
        => db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(principal, EqualityAccessPathSupport.Tenant,
            [new(EqualityAccessPathSupport.Database, EqualityAccessPathSupport.Collection, Capability.DocumentsRead | Capability.Query)], [])
        { OwnerId = principal, RestrictRows = restrictRows })).Get<PrincipalRecord>();
}
