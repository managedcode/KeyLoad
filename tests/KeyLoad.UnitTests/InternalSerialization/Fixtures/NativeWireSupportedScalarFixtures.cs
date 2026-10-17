using System.Text.Json;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeWireSupportedScalarFixtures
{
    internal const string Alias = "keyload.tests.native-wire-supported.scalar.v1";

    internal static readonly Type[] PrimitiveTypes =
    [
        typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double),
        typeof(IntPtr), typeof(UIntPtr)
    ];

    internal static readonly Type[] ScalarTypes =
    [
        typeof(object), typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
        typeof(TimeSpan), typeof(Guid), typeof(JsonElement), typeof(WellKnownStringComparerCodec)
    ];

    internal static readonly Type[] TerminalTypes =
    [.. PrimitiveTypes, .. ScalarTypes, typeof(NativeWireSupportedScalarKind)];

    internal static readonly Type[] NullableTypes = [typeof(int?), typeof(DateTimeOffset?)];

    internal static Type ArrayOf(Type element, int wrappers)
    {
        for (var index = 0; index < wrappers; index++)
        {
            element = element.MakeArrayType();
        }
        return element;
    }
}

internal enum NativeWireSupportedScalarKind : byte
{
    Unknown,
    Ready
}

// These generic-owner types are metadata probes only; the closed enum is admitted by Require's fallback.
internal static class NativeWireSupportedScalarGenericOwner<T>
{
    internal enum State : byte
    {
        Empty,
        Populated
    }
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeWireSupportedScalarFixtures.Alias)]
internal sealed record NativeWireSupportedScalarRecord(
    [property: global::Orleans.Id(0)] int? Identifier,
    [property: global::Orleans.Id(1)] NativeWireSupportedScalarKind Kind,
    [property: global::Orleans.Id(2)] string Name,
    [property: global::Orleans.Id(3)] byte[] Payload,
    [property: global::Orleans.Id(4)] List<int> Values);
