namespace KeyLoad;

/// <summary>Reports the current HTTP and command admission state.</summary>
/// <param name="Limits">The HTTP admission limits.</param>
/// <param name="Node">The node-wide command admission usage.</param>
/// <param name="VerifiedScopes">The command admission usage for verified tenant and principal scopes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.HttpAdmissionStatus)]
public sealed record HttpAdmissionStatus([property: Orleans.Id(0)] HttpAdmissionLimits Limits, [property: Orleans.Id(1)] CommandAdmissionSnapshot Node, [property: Orleans.Id(2)] CommandAdmissionSnapshot VerifiedScopes);
