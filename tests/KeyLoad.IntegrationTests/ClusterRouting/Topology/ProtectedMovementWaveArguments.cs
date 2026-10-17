using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Enrolls the existing local protected wave in its own diagnostic profile on every cold start.</summary>
internal static class ProtectedMovementWaveArguments
{
    private const string Selected = "--KeyLoadTests:ProtectedMovementProbe=true";

    internal static void Add(bool selected, LocalRf3ImageSelection.Selection? selection,
        RequestCqrsProbeFixture? controls, bool protectedDocuments,
        StorageRecovery.NativeCapabilityOmissionRf3Fixture? omission, bool eventVectorReserved,
        NativeMembershipDiagnosticFixture? membership, List<string> arguments)
    {
        if (!selected)
        {
            return;
        }
        if (selection is not null && controls is not null && protectedDocuments
            && omission is null && !eventVectorReserved && membership is null)
        {
            arguments.Add(Selected);
        }
    }
}
