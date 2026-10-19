using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class TextSelectedRecordMap
{
    internal static Dictionary<EntityRef, TextIncrementalRecord> ByReference(
        TextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        Charge(manifest, budget);
        return manifest.Records.ToDictionary(record => record.Reference);
    }

    internal static Dictionary<ulong, TextIncrementalRecord> ById(
        TextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        Charge(manifest, budget);
        return manifest.Records.ToDictionary(record => record.Id);
    }

    private static void Charge(TextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(checked((long)manifest.Records.Length * TextIncrementalSourceProtocol.MapSlotBytes));
        foreach (var record in manifest.Records)
        {
            budget.ChargeBytes(OrleansSerialization.Measure(record.Reference));
            budget.Check();
        }
    }
}
