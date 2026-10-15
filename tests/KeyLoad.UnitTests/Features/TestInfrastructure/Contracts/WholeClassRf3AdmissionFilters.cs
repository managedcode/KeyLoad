namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class WholeClassRf3AdmissionFilters
{
    internal static string For(int selection) => selection switch
    {
        0 => "/*/*/DistributedQueryAuthorityRf3Tests/*",
        1 => "/*/*/EarlyAdmissionQualificationTests/*",
        2 => "/*/*/MetadataBootstrapRf3Tests/*",
        3 => "/*/*/QueryRuleOptimizationRf3Tests/*",
        4 => "/*/*/StrongReadSessionAbsenceRf3Tests/*",
        5 => "/*/*/TimeSeriesChunkQualificationRf3Tests/*",
        6 => "/*/*/(DistributedQueryAuthorityRf3Tests|EarlyAdmissionQualificationTests|MetadataBootstrapRf3Tests|QueryRuleOptimizationRf3Tests|StrongReadSessionAbsenceRf3Tests|TimeSeriesChunkQualificationRf3Tests)/*",
        7 => "/*/*/SafeProjectorOperationRf3Tests/*",
        8 => "/*/*/SqlParserAdmissionRf3WholeFlowTests/*",
        9 => "/*/*/GraphSearchLineageRf3Tests/*",
        10 => "/*/*/AtomicTriadStagedRf3Tests/*",
        11 => "/*/*/CompetingInboxPublicTests/*",
        12 => "/*/*/QueueViewLeaseIsolationTests/*",
        13 => "/*/*/(SafeProjectorOperationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchLineageRf3Tests|AtomicTriadStagedRf3Tests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests)/*",
        _ => throw new ArgumentOutOfRangeException(nameof(selection)),
    };
}
