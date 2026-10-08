namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnIdentityReservations
{
    private const int First = 0;
    private const int Step = 1;

    internal static long FromRecords(IReadOnlyList<VectorRecord> records, AnnWorkBudget budget)
    {
        long total = First;
        for (var ordinal = First; ordinal < records.Count; ordinal++)
        {
            budget.Check();
            total = checked(total + Identity(records[ordinal].DocumentId, budget));
        }
        return total;
    }

    internal static long FromIds(string[] ids, AnnWorkBudget budget)
    {
        long total = First;
        foreach (var id in ids)
        {
            budget.Check();
            total = checked(total + Identity(id, budget));
        }
        return total;
    }

    private static long Identity(string id, AnnWorkBudget budget)
    {
        budget.Charge(checked((long)id.Length + Step));
        return PackedAnnReservations.String(id.Length);
    }
}
