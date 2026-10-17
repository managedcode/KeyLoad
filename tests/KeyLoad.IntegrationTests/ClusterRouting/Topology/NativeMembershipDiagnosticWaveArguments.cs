namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeMembershipDiagnosticWaveArguments
{
    internal static void RequireSelection(NativeMembershipDiagnosticFixture? diagnostics,
        RequestCqrsProbeFixture? controls, bool remoteDocuments, bool remoteQueries, bool protectedDocuments,
        KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Fixture? omission,
        bool eventReserved)
    {
        if (diagnostics is not null && (controls is not null || remoteDocuments || remoteQueries
            || protectedDocuments || omission is not null || eventReserved))
        {
            throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        }
    }
}
