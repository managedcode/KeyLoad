using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextSelectedRecordMap
{
    internal static Dictionary<EntityRef, NativeTextIncrementalRecord> ByReference(
        NativeTextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        Charge(manifest, budget);
        return manifest.Records.ToDictionary(record => record.Reference);
    }

    internal static Dictionary<ulong, NativeTextIncrementalRecord> ById(
        NativeTextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        Charge(manifest, budget);
        return manifest.Records.ToDictionary(record => record.Id);
    }

    private static void Charge(NativeTextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(checked((long)manifest.Records.Length * NativeTextIncrementalSourceProtocol.MapSlotBytes));
        foreach (var record in manifest.Records)
        {
            budget.ChargeBytes(NativeSerialization.Measure(record.Reference));
            budget.Check();
        }
    }
}
