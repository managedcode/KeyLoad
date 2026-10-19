using System.Collections.Immutable;
using System.Reflection;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NullableMetadataTests
{
    [Test]
    public async Task AcIs002RuntimeMetadataAlreadyDescribesTheUnderlyingNullableTypeArguments()
    {
        var context = new NullabilityInfoContext();
        var scalar = context.Create(typeof(NullableMetadataScalars).GetProperty(nameof(NullableMetadataScalars.Number))!);
        await Assert.That(scalar.Type).IsEqualTo(typeof(long?));
        await Assert.That(scalar.GenericTypeArguments.Length).IsEqualTo(0);
        var collection = context.Create(typeof(NullableMetadataCollections).GetProperty(nameof(NullableMetadataCollections.Values))!);
        await Assert.That(collection.Type).IsEqualTo(typeof(ImmutableArray<string>?));
        await Assert.That(collection.GenericTypeArguments.Length).IsEqualTo(1);
        await Assert.That(collection.GenericTypeArguments[0].Type).IsEqualTo(typeof(string));
        await Assert.That(collection.GenericTypeArguments[0].ReadState).IsEqualTo(NullabilityState.NotNull);
        var validation = ValueValidation.Create(collection);
        await Assert.That(validation.Required).IsFalse();
        await Assert.That(validation.Element!.Required).IsTrue();
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs002NullableScalarFieldsRoundtripAbsentAndPresentValues(bool present)
    {
        var expected = NullableMetadataFixtures.Scalars(present);
        var actual = OrleansSerialization.Deserialize<NullableMetadataScalars>(OrleansSerialization.Serialize(expected));
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs002PersistedPrincipalAndApiKeyOptionalDatesRoundtrip(bool present)
    {
        DateTimeOffset? date = present ? NullableMetadataFixtures.At : null;
        var principal = new PrincipalRecord(NullableMetadataFixtures.PrincipalId,
            NullableMetadataFixtures.TenantId, [], [])
        {
            ExpiresAt = date
        };
        var key = new ApiKeyRecord(NullableMetadataFixtures.ApiKeyId, principal.Id,
            NullableMetadataFixtures.Verifier, date);
        var restoredPrincipal = OrleansSerialization.Deserialize<PrincipalRecord>(OrleansSerialization.Serialize(principal));
        var restoredKey = OrleansSerialization.Deserialize<ApiKeyRecord>(OrleansSerialization.Serialize(key));
        await Assert.That(restoredPrincipal.Id).IsEqualTo(principal.Id);
        await Assert.That(restoredPrincipal.ExpiresAt).IsEqualTo(date);
        await Assert.That(restoredKey).IsEqualTo(key);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs002NullableCollectionsRetainAbsentAndNestedPresentElementPolicies(bool present)
    {
        var expected = NullableMetadataFixtures.Collections(present);
        var actual = OrleansSerialization.Deserialize<NullableMetadataCollections>(OrleansSerialization.Serialize(expected));
        await Assert.That(actual.Values.HasValue).IsEqualTo(present);
        await Assert.That(actual.Nested.HasValue).IsEqualTo(present);
        if (present)
        {
            await Assert.That(actual.Values!.Value[0]).IsEqualTo(NullableMetadataFixtures.Text);
            await Assert.That(actual.Nested!.Value[0].HasValue).IsFalse();
            await Assert.That(actual.Nested.Value[1]!.Value[0]).IsEqualTo(NullableMetadataFixtures.Text);
        }
    }

    [Test]
    public async Task AcIs002PresentNullableCollectionsRejectDefaultAndRequiredNullElements()
    {
        NullableMetadataCollections[] malformed =
        [
            new(default(ImmutableArray<string>), null),
            new(ImmutableArray.Create<string>([null!]), null),
            new(null, ImmutableArray.Create<ImmutableArray<string>?>(default(ImmutableArray<string>))),
            new(null, ImmutableArray.Create<ImmutableArray<string>?>(ImmutableArray.Create<string>([null!])))
        ];
        foreach (var value in malformed)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(value)).Code)
                .IsEqualTo(ErrorCode.Corruption);
            var bytes = NullableMetadataFixtures.EncodeUnchecked(value);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<NullableMetadataCollections>(bytes)).Code)
                .IsEqualTo(ErrorCode.Corruption);
        }
    }

    [Test]
    public async Task AcIs002RealDatabaseBootstrapPersistsNullablePrincipalAndCredentialRecords()
    {
        using var database = new TestDatabase();
        var principal = database.Store.Read(view => database.Database.Principal(view,
            NullableMetadataFixtures.BootstrapPrincipalId, NullableMetadataFixtures.At));
        await Assert.That(principal.ClusterAdministrator).IsTrue();
        await Assert.That(principal.ExpiresAt).IsNull();
        await Assert.That(database.Store.Position > 0).IsTrue();
    }
}
