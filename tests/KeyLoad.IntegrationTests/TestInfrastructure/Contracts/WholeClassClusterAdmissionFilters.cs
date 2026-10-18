namespace KeyLoad.IntegrationTests.Features.TestInfrastructure;

internal static class WholeClassClusterAdmissionFilters
{
    internal static string For(int selection) => selection switch
    {
        0 => "/*/*/EarlyAdmissionQualificationTests/*",
        1 => "/*/*/QueryRuleOptimizationClusterTests/*",
        2 => "/*/*/StrongReadSessionAbsenceClusterTests/*",
        3 => "/*/*/TimeSeriesChunkQualificationClusterTests/*",
        4 => "/*/*/(EarlyAdmissionQualificationTests|QueryRuleOptimizationClusterTests|StrongReadSessionAbsenceClusterTests|TimeSeriesChunkQualificationClusterTests)/*",
        5 => "/*/*/SafeProjectorOperationClusterTests/*",
        6 => "/*/*/SqlParserAdmissionClusterWholeFlowTests/*",
        7 => "/*/*/GraphSearchLineageClusterTests/*",
        8 => "/*/*/AtomicTriadStagedClusterTests/*",
        9 => "/*/*/CompetingInboxPublicTests/*",
        10 => "/*/*/QueueViewLeaseIsolationTests/*",
        11 => "/*/*/(SafeProjectorOperationClusterTests|SqlParserAdmissionClusterWholeFlowTests|GraphSearchLineageClusterTests|AtomicTriadStagedClusterTests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests)/*",
        12 => "/*/*/FunctionalFileOwnershipClusterTests/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens",
        _ => throw new ArgumentOutOfRangeException(nameof(selection)),
    };
}
