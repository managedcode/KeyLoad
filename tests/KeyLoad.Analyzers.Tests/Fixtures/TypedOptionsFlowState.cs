namespace KeyLoad.Analyzers.Tests.CodeQuality;

internal sealed class TypedOptionsFlowState
{
    internal int AdmittedOperations
    {
        get; set;
    }

    internal int InitialPermits
    {
        get; set;
    }

    internal TimeSpan ObservedDeadline
    {
        get; set;
    }
}
