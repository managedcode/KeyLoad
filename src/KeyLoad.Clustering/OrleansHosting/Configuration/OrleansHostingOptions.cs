namespace KeyLoad.Clustering;

/// <summary>Explicit execution Azure Table membership connection selection at host composition.</summary>
[ConfigurationOptions]
public sealed class OrleansHostingOptions
{
    /// <summary>Execution Aspire connection name shared by every silo in one Orleans deployment.</summary>
    public const string ConnectionName = "OrleansMembership";
    /// <summary>Closed startup refusal for missing execution clustering configuration.</summary>
    public const string ValidationMessage = "A execution Orleans Azure Table membership connection is required.";

    /// <summary>Gets or sets the host-selected membership connection, separate from canonical database storage.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Checks that host composition selected an explicit execution connection.</summary>
    /// <returns>Whether the connection selection is nonempty.</returns>
    public bool IsValid() => !string.IsNullOrWhiteSpace(ConnectionString);
}
