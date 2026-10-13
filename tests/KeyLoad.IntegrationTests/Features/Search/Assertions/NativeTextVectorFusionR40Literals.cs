
namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextVectorFusionR40Literals
{
    private const string FirstJson = "{\"text\":\"Київ needle needle\",\"embedding\":\"unit\",\"secret\":\"private-search-canary\",\"owner\":\"owner-a\"}";
    private const string SecondJson = "{\"text\":\"needle plain\",\"embedding\":\"unit\",\"secret\":\"private-search-canary\",\"owner\":\"owner-b\"}";
    private const string ThirdJson = "{\"text\":\"other\",\"embedding\":\"unit\",\"secret\":\"private-search-canary\",\"owner\":\"owner-b\"}";

    internal static RankedDocument[] Expected(PartitionRef partition, string query, bool restricted,
        bool text, bool vector, bool explain)
    {
        var rows = !text
            ? restricted ? [("b", 0, 1), ("c", 0, 2)] : [("a", 0, 1), ("b", 0, 2), ("c", 0, 3)]
            : !vector ? TextRows(query, restricted) : FusionRows(query, restricted);
        return rows.Select(row => Row(partition, row.Id, row.Text, row.Vector, explain)).ToArray();
    }

    private static (string Id, int Text, int Vector)[] TextRows(string query, bool restricted)
    {
        if (query == "plain")
        { return [("b", 1, 0)]; }
        if (query == "КИЇВ")
        { return restricted ? [] : [("a", 1, 0)]; }
        return restricted ? [("b", 1, 0)] : [("a", 1, 0), ("b", 2, 0)];
    }

    private static (string Id, int Text, int Vector)[] FusionRows(string query, bool restricted)
    {
        if (restricted)
        { return query == "КИЇВ" ? [("b", 0, 1), ("c", 0, 2)] : [("b", 1, 1), ("c", 0, 2)]; }
        if (query == "plain")
        { return [("b", 1, 2), ("a", 0, 1), ("c", 0, 3)]; }
        if (query == "КИЇВ")
        { return [("a", 1, 1), ("b", 0, 2), ("c", 0, 3)]; }
        return [("a", 1, 1), ("b", 2, 2), ("c", 0, 3)];
    }

    private static RankedDocument Row(PartitionRef partition, string id, int textRank, int vectorRank,
        bool explain)
    {
        var json = id == "a" ? FirstJson : id == "b" ? SecondJson : ThirdJson;
        var parts = new List<SearchBranchContribution>();
        if (textRank != 0)
        { parts.Add(new(SearchBranchKind.Text, textRank, 1d, 1d / (60d + textRank))); }
        if (vectorRank != 0)
        { parts.Add(new(SearchBranchKind.Vector, vectorRank, 1d, 1d / (60d + vectorRank))); }
        var score = (textRank == 0 ? 0d : 1d / (60d + textRank)) + (vectorRank == 0 ? 0d : 1d / (60d + vectorRank));
        return new(new(new(partition, NativeTextRf3Scenario.Collection, id), 1L, json, false, []), score, explain ? new(60, [.. parts]) : null);
    }
}
