namespace KeyLoad;

/// <summary>Configures node-local admission limits without changing canonical apply decisions.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommandAdmissionLimits)]
public sealed record CommandAdmissionLimits
{
    /// <summary>Gets or sets the maximum number of admitted commands.</summary>
    [Orleans.Id(0)]
    public int MaxCommands { get; init; } = 256;
    /// <summary>Gets or sets the maximum bytes retained by admitted commands.</summary>
    [Orleans.Id(1)]
    public long MaxRetainedBytes { get; init; } = 134_217_728;
    /// <summary>Gets or sets the maximum number of admitted commands per tenant.</summary>
    [Orleans.Id(2)]
    public int MaxTenantCommands { get; init; } = 128;
    /// <summary>Gets or sets the maximum number of admitted commands per principal.</summary>
    [Orleans.Id(3)]
    public int MaxPrincipalCommands { get; init; } = 64;
    /// <summary>Gets or sets the command capacity reserved for control operations.</summary>
    [Orleans.Id(4)]
    public int ReservedControlCommands { get; init; } = 16;
    /// <summary>Gets or sets the byte capacity reserved for control operations.</summary>
    [Orleans.Id(5)]
    public long ReservedControlBytes { get; init; } = 2_097_152;
    /// <summary>Gets or sets the maximum payload size for a control command, in bytes.</summary>
    [Orleans.Id(6)]
    public int MaxControlPayloadBytes { get; init; } = 65_536;
    /// <summary>Gets or sets the maximum number of control commands per tenant.</summary>
    [Orleans.Id(7)]
    public int MaxTenantControlCommands { get; init; } = 8;
    /// <summary>Gets or sets the maximum number of control commands per principal.</summary>
    [Orleans.Id(8)]
    public int MaxPrincipalControlCommands { get; init; } = 8;

    /// <summary>Validates that all configured limits are positive.</summary>
    /// <exception cref="ArgumentException">One or more configured limits are not positive.</exception>
    public void Validate()
    {
        if (MaxCommands < 1 || MaxRetainedBytes < 1 || MaxTenantCommands < 1 || MaxPrincipalCommands < 1
            || ReservedControlCommands < 1 || ReservedControlBytes < 1 || MaxControlPayloadBytes < 1
            || MaxTenantControlCommands < 1 || MaxPrincipalControlCommands < 1)
        {
            throw new ArgumentException("Command admission limits must be positive.");
        }
    }
}

/// <summary>Reports the current command admission usage for a node.</summary>
/// <param name="Commands">Number of currently admitted commands.</param>
/// <param name="RetainedBytes">Bytes currently retained by admitted commands.</param>
/// <param name="ControlCommands">Number of currently admitted control commands.</param>
/// <param name="ControlRetainedBytes">Bytes currently retained by control commands.</param>
/// <param name="ActiveTenantScopes">Number of tenants with active admission scopes.</param>
/// <param name="ActivePrincipalScopes">Number of principals with active admission scopes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommandAdmissionSnapshot)]
public sealed record CommandAdmissionSnapshot([property: Orleans.Id(0)] int Commands, [property: Orleans.Id(1)] long RetainedBytes, [property: Orleans.Id(2)] int ControlCommands, [property: Orleans.Id(3)] long ControlRetainedBytes,
    [property: Orleans.Id(4)] int ActiveTenantScopes, [property: Orleans.Id(5)] int ActivePrincipalScopes);

/// <summary>Combines node command admission limits and their current usage.</summary>
/// <param name="Limits">The node-local command admission limits.</param>
/// <param name="Usage">The current command admission usage snapshot.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.NodeAdmissionStatus)]
public sealed record NodeAdmissionStatus([property: Orleans.Id(0)] CommandAdmissionLimits Limits, [property: Orleans.Id(1)] CommandAdmissionSnapshot Usage)
{
    /// <summary>Gets or initializes the HTTP admission status when HTTP admission is configured.</summary>
    [Orleans.Id(2)]
    public HttpAdmissionStatus? Http { get; init; }
}
