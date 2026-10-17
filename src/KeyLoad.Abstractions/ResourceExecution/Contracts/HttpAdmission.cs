namespace KeyLoad;

/// <summary>Configures node-local HTTP request admission limits.</summary>
[ConfigurationOptions]
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.HttpAdmissionLimits)]
public sealed record HttpAdmissionLimits
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:HttpAdmission";
    /// <summary>The startup rejection for invalid admission settings.</summary>
    public const string ValidationMessage = "HTTP admission limits are invalid.";
    private const int MinimumPositiveBudget = 1;
    private const int DefaultMaxRequests = 32;
    private const long DefaultMaxReservedBytes = 1_073_741_824;
    private const int DefaultMaxTenantRequests = 16;
    private const int DefaultMaxPrincipalRequests = 8;
    private const int DefaultReservedControlRequests = 16;
    private const long DefaultReservedControlBytes = 8_388_608;
    private const int DefaultMaxTenantControlRequests = 8;
    private const int DefaultMaxPrincipalControlRequests = 8;
    private const int DefaultMaxBodyBytes = 8_388_608;
    private const int DefaultMaxControlBodyBytes = 65_536;
    private const long DefaultHeavyReadReservedBytes = 67_108_864;
    private const long DefaultOtherReservedBytes = 8_388_608;
    private const int NoReservedBytes = 0;
    private const int MaximumBodyBytes = 33_554_432;
    private const long MaximumReadReservationBytes = 1_073_741_824;

    /// <summary>Gets or sets the maximum number of admitted HTTP requests.</summary>
    [Orleans.Id(0)]
    public int MaxRequests { get; init; } = DefaultMaxRequests;
    /// <summary>Gets or sets the maximum bytes reserved by admitted HTTP requests.</summary>
    [Orleans.Id(1)]
    public long MaxReservedBytes { get; init; } = DefaultMaxReservedBytes;
    /// <summary>Gets or sets the maximum number of admitted requests per tenant.</summary>
    [Orleans.Id(2)]
    public int MaxTenantRequests { get; init; } = DefaultMaxTenantRequests;
    /// <summary>Gets or sets the maximum number of admitted requests per principal.</summary>
    [Orleans.Id(3)]
    public int MaxPrincipalRequests { get; init; } = DefaultMaxPrincipalRequests;
    /// <summary>Gets or sets the request capacity reserved for control operations.</summary>
    [Orleans.Id(4)]
    public int ReservedControlRequests { get; init; } = DefaultReservedControlRequests;
    /// <summary>Gets or sets the byte capacity reserved for control operations.</summary>
    [Orleans.Id(5)]
    public long ReservedControlBytes { get; init; } = DefaultReservedControlBytes;
    /// <summary>Gets or sets the maximum number of control requests per tenant.</summary>
    [Orleans.Id(6)]
    public int MaxTenantControlRequests { get; init; } = DefaultMaxTenantControlRequests;
    /// <summary>Gets or sets the maximum number of control requests per principal.</summary>
    [Orleans.Id(7)]
    public int MaxPrincipalControlRequests { get; init; } = DefaultMaxPrincipalControlRequests;
    /// <summary>Gets or sets the maximum HTTP request body size, in bytes.</summary>
    [Orleans.Id(8)]
    public int MaxBodyBytes { get; init; } = DefaultMaxBodyBytes;
    /// <summary>Gets or sets the maximum control request body size, in bytes.</summary>
    [Orleans.Id(9)]
    public int MaxControlBodyBytes { get; init; } = DefaultMaxControlBodyBytes;
    /// <summary>Gets or sets the reserved bytes for heavy read operations.</summary>
    [Orleans.Id(10)]
    public long HeavyReadReservedBytes { get; init; } = DefaultHeavyReadReservedBytes;
    /// <summary>Gets or sets the reserved bytes for other operations.</summary>
    [Orleans.Id(11)]
    public long OtherReservedBytes { get; init; } = DefaultOtherReservedBytes;

    /// <summary>Validates the configured HTTP admission limits and body-size relationships.</summary>
    /// <exception cref="ArgumentException">One or more configured limits are invalid.</exception>
    public void Validate()
    {
        if (MaxRequests < MinimumPositiveBudget || MaxReservedBytes < MinimumPositiveBudget || MaxTenantRequests < MinimumPositiveBudget || MaxPrincipalRequests < MinimumPositiveBudget
            || ReservedControlRequests < MinimumPositiveBudget || ReservedControlBytes < MinimumPositiveBudget || MaxTenantControlRequests < MinimumPositiveBudget || MaxPrincipalControlRequests < MinimumPositiveBudget
            || MaxBodyBytes is < MinimumPositiveBudget or > MaximumBodyBytes || MaxControlBodyBytes < MinimumPositiveBudget || MaxControlBodyBytes > MaxBodyBytes
            || HeavyReadReservedBytes is < NoReservedBytes or > MaximumReadReservationBytes || OtherReservedBytes is < NoReservedBytes or > MaximumReadReservationBytes)
        {
            throw new ArgumentException(ValidationMessage);
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
