namespace KeyLoad.UnitTests.Features.InternalSerialization;

[Orleans.GenerateSerializer, Orleans.Alias(ImmutableDictionaryUnknownComparer.ContractAlias)]
internal sealed class ImmutableDictionaryUnknownComparer : IEqualityComparer<string>
{
    internal const string ContractAlias = "keyload.test.execution-immutable-map-unknown-comparer";

    public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
}
