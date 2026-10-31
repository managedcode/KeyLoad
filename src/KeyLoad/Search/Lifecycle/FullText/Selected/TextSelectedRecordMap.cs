using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextSelectedRecordMap
{
    internal static Dictionary<EntityRef, TextIncrementalRecord> ByReference(
        TextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        budget.Check();
        return manifest.Records.ToDictionary(record => record.Reference);
    }

    internal static Dictionary<ulong, TextIncrementalRecord> ById(
        TextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        budget.Check();
        return manifest.Records.ToDictionary(record => record.Id);
    }
}
