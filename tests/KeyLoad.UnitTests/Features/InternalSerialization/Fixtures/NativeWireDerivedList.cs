namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeWireDerivedList.ContractAlias)]
internal sealed class NativeWireDerivedList : List<float>
{
    internal const string ContractAlias = "keyload.tests.native.derived-list.v1";
}
