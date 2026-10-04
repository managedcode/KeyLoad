using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleAggregateAuthorizationTests
{
    [Test]
    public async Task AcSeries011AllReadersRequireCurrentPersistedSeriesReadEvenWhenEmpty()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.GrantRead(db, SampleAggregateTestData.ReaderPrincipal);
        var instant = SampleAggregateTestData.Start;

        var latest = db.Database.ReadLatestSample(SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.EmptySeries));
        var aggregate = db.Database.AggregateSamples(SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.EmptySeries, instant, instant));
        var windows = db.Database.AggregateSampleWindows(SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.EmptySeries,
                instant, instant.AddTicks(1), SampleAggregateTestData.Minute));

        await Assert.That(latest.Sample).IsNull();
        await Assert.That(aggregate.Count).IsEqualTo(0L);
        await Assert.That(windows.Windows).HasSingleItem();
        SampleAggregateTestData.Failure(() => db.Database.ReadLatestSample(
            SampleAggregateTestData.RevokedPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.EmptySeries)), ErrorCode.Unauthenticated);
        SampleAggregateTestData.Failure(() => db.Database.AggregateSamples(
            SampleAggregateTestData.RevokedPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.EmptySeries, instant, instant)),
            ErrorCode.Unauthenticated);
        SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.RevokedPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.EmptySeries,
                instant, instant, SampleAggregateTestData.Minute)), ErrorCode.Unauthenticated);
    }

    [Test]
    public async Task AcSeries011LatestTagsAndNumericReadersUseCurrentPersistedFieldPolicies()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db, [new(SampleAggregateTestData.ProtectedPath,
            SampleAggregateTestData.PrivateClassification, SampleAggregateTestData.PrivateGrant)]);
        SampleAggregateTestData.GrantRead(db, SampleAggregateTestData.ReaderPrincipal);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Samples(SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, 7)),
            SampleAggregateTestData.ProtectedTags);

        var latest = db.Database.ReadLatestSample(SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        var aggregate = db.Database.AggregateSamples(SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleAggregateTestData.Start, SampleAggregateTestData.Start.AddTicks(1)));
        var withPrivateGrant = SampleAggregateTestData.ReaderPrincipal + SampleAggregateTestData.PrivatePrincipalSuffix;
        SampleAggregateTestData.GrantRead(db, withPrivateGrant, includePrivate: true);
        var privateLatest = db.Database.ReadLatestSample(withPrivateGrant,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));

        await Assert.That(latest.Sample?.TagsJson).IsEqualTo(SampleAggregateTestData.ExpectedProjectedTags);
        await Assert.That(latest.Sample?.TagsJson).DoesNotContain(SampleAggregateTestData.SensitiveValue);
        await Assert.That(aggregate.Count).IsEqualTo(1L);
        await Assert.That(privateLatest.Sample?.TagsJson).IsEqualTo(SampleAggregateTestData.ProtectedTags);
    }

    [Test]
    public async Task AcSeries011PersistedRevocationDeniesEveryNewReadOperation()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.GrantRead(db, SampleAggregateTestData.ReaderPrincipal);
        SampleAggregateTestData.Append(db, SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, 1));
        var principalKey = KeySpace.Principal(SampleAggregateTestData.ReaderPrincipal);
        var current = db.Store.Read(view => view.GetRecord<PrincipalRecord>(principalKey)!);
        db.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(current with { Revoked = true, PolicyEpoch = current.PolicyEpoch + 1 }));
        var position = db.Store.Position;

        var latest = SampleAggregateTestData.Failure(() => db.Database.ReadLatestSample(
            SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series)), ErrorCode.Unauthenticated);
        var aggregate = SampleAggregateTestData.Failure(() => db.Database.AggregateSamples(
            SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleAggregateTestData.Start)), ErrorCode.Unauthenticated);
        var windows = SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.ReaderPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleAggregateTestData.Start, SampleAggregateTestData.Start.AddTicks(1), SampleAggregateTestData.Minute)),
            ErrorCode.Unauthenticated);

        await Assert.That(latest.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(aggregate.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(windows.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }
}
