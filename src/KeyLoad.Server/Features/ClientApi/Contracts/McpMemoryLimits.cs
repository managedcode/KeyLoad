namespace KeyLoad.Server;

/// <summary>Independent node-local retained-allocation budgets for the official MCP adapter.</summary>
[ConfigurationOptions]
internal sealed record McpMemoryLimits
{
    private const long DefaultDataBytes = 2_147_483_648;
    private const long DefaultControlBytes = 134_217_728;
    private const long DefaultIngressBytes = 1_073_741_824;

    /// <summary>Capacity for classified data operations and their native output ownership.</summary>
    public long DataBytes { get; init; } = DefaultDataBytes;
    /// <summary>Capacity reserved for classified control operations and protocol responses.</summary>
    public long ControlBytes { get; init; } = DefaultControlBytes;
    /// <summary>Capacity for authenticated but not yet classified bounded wire processing.</summary>
    public long IngressBytes { get; init; } = DefaultIngressBytes;

    /// <summary>Rejects invalid configuration before the server opens its physical stores.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ControlBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(IngressBytes);
    }
}
