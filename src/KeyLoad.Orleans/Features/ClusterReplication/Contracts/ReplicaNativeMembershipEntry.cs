namespace KeyLoad.Orleans;

/// <summary>One bounded actual Active native silo name and complete generation address.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaNativeMembershipEntry.NativeAlias)]
public sealed record ReplicaNativeMembershipEntry(
    [property: global::Orleans.Id(0)] string SiloName,
    [property: global::Orleans.Id(1)] string SiloAddress)
{
    internal const string NativeAlias = "keyload.replica.native-membership-entry.v1";
}
