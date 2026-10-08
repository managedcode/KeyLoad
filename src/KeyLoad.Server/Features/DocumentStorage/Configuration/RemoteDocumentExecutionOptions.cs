namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>Bounds native receiving/source remote-document frame admission.</summary>
[ConfigurationOptions]
internal sealed class RemoteDocumentExecutionOptions
{
    internal const string SectionName = "KeyLoad:RemoteDocumentExecution";
    internal const string ValidationMessage = "Remote document admission must be between one and eight.";
    private const int MinimumAdmissions = 1;
    private const int AdmissionCeiling = 8;
    /// <summary>Maximum original owned transport frames at one physical node.</summary>
    public int MaximumAdmissions { get; set; } = AdmissionCeiling;
    internal bool IsValid() => MaximumAdmissions is >= MinimumAdmissions and <= AdmissionCeiling;
}
