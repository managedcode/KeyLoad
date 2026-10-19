using System.Text.Json;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class WireSupportedScalarFixtures
{
    internal const string Alias = "keyload.tests.execution-wire-supported.scalar";

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
    [.. PrimitiveTypes, .. ScalarTypes, typeof(WireSupportedScalarKind)];

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
