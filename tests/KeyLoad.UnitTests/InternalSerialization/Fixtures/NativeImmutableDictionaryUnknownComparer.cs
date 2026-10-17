namespace KeyLoad.UnitTests.Features.InternalSerialization;

[Orleans.GenerateSerializer, Orleans.Alias(NativeImmutableDictionaryUnknownComparer.ContractAlias)]
internal sealed class NativeImmutableDictionaryUnknownComparer : IEqualityComparer<string>
{
    internal const string ContractAlias = "keyload.test.native-immutable-map-unknown-comparer.v1";

    public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
}
