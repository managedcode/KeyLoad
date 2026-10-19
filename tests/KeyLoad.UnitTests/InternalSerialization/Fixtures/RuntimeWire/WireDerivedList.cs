namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(WireDerivedList.ContractAlias)]
internal sealed class WireDerivedList : List<float>
{
    internal const string ContractAlias = "keyload.tests.execution.derived-list";
}
