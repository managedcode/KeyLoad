namespace KeyLoad;

/// <summary>Combines node command admission limits and their current usage.</summary>
/// <param name="Limits">The node-local command admission limits.</param>
/// <param name="Usage">The current command admission usage snapshot.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.NodeAdmissionStatus)]
public sealed record NodeAdmissionStatus([property: Orleans.Id(0)] CommandAdmissionLimits Limits, [property: Orleans.Id(1)] CommandAdmissionSnapshot Usage)
{
    /// <summary>Gets or initializes the HTTP admission status when HTTP admission is configured.</summary>
    [Orleans.Id(2)]
    public HttpAdmissionStatus? Http
    {
        get; init;
    }
}
