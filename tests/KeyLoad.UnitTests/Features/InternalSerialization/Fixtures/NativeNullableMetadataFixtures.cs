using System.Collections.Immutable;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeNullableMetadataFixtures
{
    internal const string ScalarsAlias = "keyload.tests.native-nullable.scalars.v1";
    internal const string CollectionsAlias = "keyload.tests.native-nullable.collections.v1";
    internal const string PrincipalId = "nullable-principal";
    internal const string TenantId = "nullable-tenant";
    internal const string ApiKeyId = "nullable-api-key";
    internal const string Verifier = "nullable-verifier";
    internal const string Text = "required-element";
    internal const string BootstrapPrincipalId = "root";
    internal const long Number = 123;
    internal static DateTimeOffset At => DateTimeOffset.UnixEpoch;

    internal static NativeNullableMetadataScalars Scalars(bool present)
        => new(present ? Number : null, present ? At : null, present ? ErrorCode.Validation : null);

    internal static NativeNullableMetadataCollections Collections(bool present)
        => present ? new([Text], [null, ImmutableArray.Create(Text)]) : new(null, null);

    internal static byte[] EncodeUnchecked<T>(T value)
        => NativeSerializerProviders.Get(typeof(T)).Serializer.SerializeToArray(
            new NativePayload { Version = NativePayloadVersion.Current, Value = value });
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeNullableMetadataFixtures.ScalarsAlias)]
internal sealed record NativeNullableMetadataScalars(
    [property: global::Orleans.Id(0)] long? Number,
    [property: global::Orleans.Id(1)] DateTimeOffset? Date,
    [property: global::Orleans.Id(2)] ErrorCode? Error);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeNullableMetadataFixtures.CollectionsAlias)]
internal sealed record NativeNullableMetadataCollections(
    [property: global::Orleans.Id(0)] ImmutableArray<string>? Values,
    [property: global::Orleans.Id(1)] ImmutableArray<ImmutableArray<string>?>? Nested);
