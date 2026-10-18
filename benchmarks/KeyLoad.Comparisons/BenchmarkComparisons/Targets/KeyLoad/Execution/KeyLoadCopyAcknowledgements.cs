namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadCopyAcknowledgements
{
    internal static void Require(CommitReceipt receipt, int expectedCopies)
    {
        if (receipt.Durability != DurabilityProfile.ProcessDurable || receipt.PersistedCopies != expectedCopies)
        {
            throw new ComparisonFailureException(KeyLoadEventOperations.WrongWriteProfile);
        }
    }
}
