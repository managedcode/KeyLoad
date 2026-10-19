using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NullableMetadataFixtures
{
    internal const string ScalarsAlias = "keyload.tests.execution-nullable.scalars";
    internal const string CollectionsAlias = "keyload.tests.execution-nullable.collections";
    internal const string PrincipalId = "nullable-principal";
    internal const string TenantId = "nullable-tenant";
    internal const string ApiKeyId = "nullable-api-key";
    internal const string Verifier = "nullable-verifier";
    internal const string Text = "required-element";
    internal const string BootstrapPrincipalId = "root";
    internal const long Number = 123;
    internal static DateTimeOffset At => DateTimeOffset.UnixEpoch;

    internal static NullableMetadataScalars Scalars(bool present)
        => new(present ? Number : null, present ? At : null, present ? ErrorCode.Validation : null);

    internal static NullableMetadataCollections Collections(bool present)
        => present ? new([Text], [null, ImmutableArray.Create(Text)]) : new(null, null);

    internal static byte[] EncodeUnchecked<T>(T value)
        => SerializerProviders.Get(typeof(T)).Serializer.SerializeToArray(
            new RuntimePayload { Version = PayloadVersion.Current, Value = value });
}
