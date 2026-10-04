namespace KeyLoad;

/// <summary>Configures node-local HTTP request admission limits.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.HttpAdmissionLimits)]
public sealed record HttpAdmissionLimits
{
    /// <summary>Gets or sets the maximum number of admitted HTTP requests.</summary>
    [Orleans.Id(0)]
    public int MaxRequests { get; init; } = 32;
    /// <summary>Gets or sets the maximum bytes reserved by admitted HTTP requests.</summary>
    [Orleans.Id(1)]
    public long MaxReservedBytes { get; init; } = 1_073_741_824;
    /// <summary>Gets or sets the maximum number of admitted requests per tenant.</summary>
    [Orleans.Id(2)]
    public int MaxTenantRequests { get; init; } = 16;
    /// <summary>Gets or sets the maximum number of admitted requests per principal.</summary>
    [Orleans.Id(3)]
    public int MaxPrincipalRequests { get; init; } = 8;
    /// <summary>Gets or sets the request capacity reserved for control operations.</summary>
    [Orleans.Id(4)]
    public int ReservedControlRequests { get; init; } = 16;
    /// <summary>Gets or sets the byte capacity reserved for control operations.</summary>
    [Orleans.Id(5)]
    public long ReservedControlBytes { get; init; } = 8_388_608;
    /// <summary>Gets or sets the maximum number of control requests per tenant.</summary>
    [Orleans.Id(6)]
    public int MaxTenantControlRequests { get; init; } = 8;
    /// <summary>Gets or sets the maximum number of control requests per principal.</summary>
    [Orleans.Id(7)]
    public int MaxPrincipalControlRequests { get; init; } = 8;
    /// <summary>Gets or sets the maximum HTTP request body size, in bytes.</summary>
    [Orleans.Id(8)]
    public int MaxBodyBytes { get; init; } = 8_388_608;
    /// <summary>Gets or sets the maximum control request body size, in bytes.</summary>
    [Orleans.Id(9)]
    public int MaxControlBodyBytes { get; init; } = 65_536;
    /// <summary>Gets or sets the reserved bytes for heavy read operations.</summary>
    [Orleans.Id(10)]
    public long HeavyReadReservedBytes { get; init; } = 67_108_864;
    /// <summary>Gets or sets the reserved bytes for other operations.</summary>
    [Orleans.Id(11)]
    public long OtherReservedBytes { get; init; } = 8_388_608;

    /// <summary>Validates the configured HTTP admission limits and body-size relationships.</summary>
    /// <exception cref="ArgumentException">One or more configured limits are invalid.</exception>
    public void Validate()
    {
        if (MaxRequests < 1 || MaxReservedBytes < 1 || MaxTenantRequests < 1 || MaxPrincipalRequests < 1
            || ReservedControlRequests < 1 || ReservedControlBytes < 1 || MaxTenantControlRequests < 1 || MaxPrincipalControlRequests < 1
            || MaxBodyBytes is < 1 or > 33_554_432 || MaxControlBodyBytes < 1 || MaxControlBodyBytes > MaxBodyBytes
            || HeavyReadReservedBytes is < 0 or > 1_073_741_824 || OtherReservedBytes is < 0 or > 1_073_741_824)
        {
            throw new ArgumentException("HTTP admission limits are invalid.");
        }
    }
}

/// <summary>Reports the current HTTP and command admission state.</summary>
/// <param name="Limits">The HTTP admission limits.</param>
/// <param name="Node">The node-wide command admission usage.</param>
/// <param name="VerifiedScopes">The command admission usage for verified tenant and principal scopes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.HttpAdmissionStatus)]
public sealed record HttpAdmissionStatus([property: Orleans.Id(0)] HttpAdmissionLimits Limits, [property: Orleans.Id(1)] CommandAdmissionSnapshot Node, [property: Orleans.Id(2)] CommandAdmissionSnapshot VerifiedScopes);
