namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>Actual marked options used by the native registration and execution regression.</summary>
[KeyLoad.ConfigurationOptions]
public sealed class TypedOptionsFlowSettings
{
    /// <summary>Maximum concurrent admissions captured by the consumer.</summary>
    public int AdmissionCapacity { get; set; } = 64;

    /// <summary>Deadline passed to the native task operation.</summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(30);
}
