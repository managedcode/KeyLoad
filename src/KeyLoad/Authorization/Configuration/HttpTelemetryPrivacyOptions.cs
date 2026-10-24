namespace KeyLoad.Server.Authorization.Configuration;

/// <summary>Validated work bounds for shared HTTP telemetry privacy.</summary>
[KeyLoad.ConfigurationOptions]
internal sealed class HttpTelemetryPrivacyOptions
{
    /// <summary>The configuration section containing privacy processing budgets.</summary>
    public const string SectionName = "KeyLoad:HttpTelemetryPrivacy";
    /// <summary>The validation failure for unsafe processing bounds.</summary>
    public const string ValidationMessage = "HTTP telemetry privacy bounds exceed the reviewed contract.";
    private const int DefaultMaximumTags = 32;
    private const int DefaultMaximumBaggageItems = 8;
    private const int DefaultMaximumParentLinks = 32;
    /// <summary>Gets or sets the maximum span tags examined and removed.</summary>
    public int MaximumTags { get; set; } = DefaultMaximumTags;
    /// <summary>Gets or sets the maximum local ancestor links examined before inherited baggage access.</summary>
    public int MaximumParentLinks { get; set; } = DefaultMaximumParentLinks;
    /// <summary>Gets or sets the maximum baggage keys removed.</summary>
    public int MaximumBaggageItems { get; set; } = DefaultMaximumBaggageItems;
    /// <summary>Checks every bound against the closed reviewed limits.</summary>
    /// <returns>Whether the supplied bounds are safe.</returns>
    public bool IsValid() => MaximumTags is >= 1 and <= DefaultMaximumTags
        && MaximumBaggageItems is >= 0 and <= DefaultMaximumBaggageItems
        && MaximumParentLinks is >= 0 and <= DefaultMaximumParentLinks;
}
