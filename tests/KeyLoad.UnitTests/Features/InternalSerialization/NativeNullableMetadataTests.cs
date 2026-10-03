using System.Collections.Immutable;
using System.Reflection;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeNullableMetadataTests
{
    [Test]
    public async Task AcIs002RuntimeMetadataAlreadyDescribesTheUnderlyingNullableTypeArguments()
    {
        var context = new NullabilityInfoContext();
        var scalar = context.Create(typeof(NativeNullableMetadataScalars).GetProperty(nameof(NativeNullableMetadataScalars.Number))!);
        await Assert.That(scalar.Type).IsEqualTo(typeof(long?));
        await Assert.That(scalar.GenericTypeArguments.Length).IsEqualTo(0);
        var collection = context.Create(typeof(NativeNullableMetadataCollections).GetProperty(nameof(NativeNullableMetadataCollections.Values))!);
        await Assert.That(collection.Type).IsEqualTo(typeof(ImmutableArray<string>?));
        await Assert.That(collection.GenericTypeArguments.Length).IsEqualTo(1);
        await Assert.That(collection.GenericTypeArguments[0].Type).IsEqualTo(typeof(string));
        await Assert.That(collection.GenericTypeArguments[0].ReadState).IsEqualTo(NullabilityState.NotNull);
        var validation = NativeValueValidation.Create(collection);
        await Assert.That(validation.Required).IsFalse();
        await Assert.That(validation.Element!.Required).IsTrue();
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs002NullableScalarFieldsRoundtripAbsentAndPresentValues(bool present)
    {
        var expected = NativeNullableMetadataFixtures.Scalars(present);
        var actual = NativeSerialization.Deserialize<NativeNullableMetadataScalars>(NativeSerialization.Serialize(expected));
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs002PersistedPrincipalAndApiKeyOptionalDatesRoundtrip(bool present)
    {
        DateTimeOffset? date = present ? NativeNullableMetadataFixtures.At : null;
        var principal = new PrincipalRecord(NativeNullableMetadataFixtures.PrincipalId,
            NativeNullableMetadataFixtures.TenantId, [], []) { ExpiresAt = date };
        var key = new ApiKeyRecord(NativeNullableMetadataFixtures.ApiKeyId, principal.Id,
            NativeNullableMetadataFixtures.Verifier, date);
        var restoredPrincipal = NativeSerialization.Deserialize<PrincipalRecord>(NativeSerialization.Serialize(principal));
        var restoredKey = NativeSerialization.Deserialize<ApiKeyRecord>(NativeSerialization.Serialize(key));
        await Assert.That(restoredPrincipal.Id).IsEqualTo(principal.Id);
        await Assert.That(restoredPrincipal.ExpiresAt).IsEqualTo(date);
        await Assert.That(restoredKey).IsEqualTo(key);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs002NullableCollectionsRetainAbsentAndNestedPresentElementPolicies(bool present)
    {
        var expected = NativeNullableMetadataFixtures.Collections(present);
        var actual = NativeSerialization.Deserialize<NativeNullableMetadataCollections>(NativeSerialization.Serialize(expected));
        await Assert.That(actual.Values.HasValue).IsEqualTo(present);
        await Assert.That(actual.Nested.HasValue).IsEqualTo(present);
        if (present)
        {
            await Assert.That(actual.Values!.Value[0]).IsEqualTo(NativeNullableMetadataFixtures.Text);
            await Assert.That(actual.Nested!.Value[0].HasValue).IsFalse();
            await Assert.That(actual.Nested.Value[1]!.Value[0]).IsEqualTo(NativeNullableMetadataFixtures.Text);
        }
    }

    [Test]
    public async Task AcIs002PresentNullableCollectionsRejectDefaultAndRequiredNullElements()
    {
        NativeNullableMetadataCollections[] malformed =
        [
            new(default(ImmutableArray<string>), null),
            new(ImmutableArray.Create<string>([null!]), null),
            new(null, ImmutableArray.Create<ImmutableArray<string>?>(default(ImmutableArray<string>))),
            new(null, ImmutableArray.Create<ImmutableArray<string>?>(ImmutableArray.Create<string>([null!])))
        ];
        foreach (var value in malformed)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(value)).Code)
                .IsEqualTo(ErrorCode.Corruption);
            var bytes = NativeNullableMetadataFixtures.EncodeUnchecked(value);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<NativeNullableMetadataCollections>(bytes)).Code)
                .IsEqualTo(ErrorCode.Corruption);
        }
    }

    [Test]
    public async Task AcIs002RealDatabaseBootstrapPersistsNullablePrincipalAndCredentialRecords()
    {
        using var database = new TestDatabase();
        var principal = database.Store.Read(view => database.Database.Principal(view,
            NativeNullableMetadataFixtures.BootstrapPrincipalId, NativeNullableMetadataFixtures.At));
        await Assert.That(principal.ClusterAdministrator).IsTrue();
        await Assert.That(principal.ExpiresAt).IsNull();
        await Assert.That(database.Store.Position > 0).IsTrue();
    }
}
