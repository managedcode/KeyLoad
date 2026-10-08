namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Centrally bounded admission for configured physical-owner proofs.</summary>
[ConfigurationOptions]
internal sealed class PhysicalOwnerExecutionOptions
{
    internal const string SectionName = "KeyLoad:PhysicalOwnerExecution";
    internal const string ValidationMessage = "Physical owner proof admission must be between one and eight.";
    private const int MinimumAdmissions = 1;
    private const int AdmissionCeiling = 8;

    /// <summary>Maximum simultaneously admitted native proof requests.</summary>
    public int MaximumAdmissions { get; set; } = AdmissionCeiling;

    internal bool IsValid() => MaximumAdmissions is >= MinimumAdmissions and <= AdmissionCeiling;
}
