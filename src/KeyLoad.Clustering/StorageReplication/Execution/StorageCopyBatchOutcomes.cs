namespace KeyLoad.Clustering;

/// <summary>Turns the copies' batch results, or a batch failure, into each submitted command's outcome.</summary>
internal static class StorageCopyBatchOutcomes
{
    /// <summary>Acknowledges the prefix every copy applied with the same sequences and results.</summary>
    /// <returns>True when all copies agree, so the owners' cut can advance to the batch position.</returns>
    internal static bool Accept(StorageBatchResult[] results, StorageCopyOutcome[] outcomes)
    {
        var original = results[StorageCopyContract.FirstOwner];
        var applied = results.Max(result => result.Applied.Length);
        if (results.Any(result => result.Position != original.Position || result.Applied.Length != applied))
        {
            Differ(outcomes, StorageCopyContract.FirstOwner, applied);
            return false;
        }

        for (var index = StorageCopyContract.FirstOwner; index < applied; index++)
        {
            var expected = original.Applied[index];
            if (results.Any(result => result.Applied[index].Sequence != expected.Sequence
                || !StorageCopyAcknowledgements.Same(result.Applied[index].Result, expected.Result)))
            {
                Differ(outcomes, index, applied);
                return false;
            }

            outcomes[index] = StorageCopyOutcome.Completed(StorageCopyAcknowledgements.Attach(expected.Result, results.Length));
        }

        return true;
    }

    /// <summary>Assigns a batch failure. Before the copies receive the batch, no command was applied: a transport
    /// failure or an uncertain owner leaves every command with OwnershipLost, so a retry of the same command is safe.
    /// Once the copies received it, an uncertain write fails every command they received. A failure every copy raised
    /// before any effect belongs to the first command or, when the batch could not start, to every command. Commands no
    /// copy could have applied stay undecided.</summary>
    /// <param name="outcomes">The batch's command outcomes.</param>
    /// <param name="sent">The number of commands the copies received.</param>
    /// <param name="error">The original failure.</param>
    /// <param name="persisted">Whether at least one copy acknowledged its commit.</param>
    /// <param name="started">Whether the failure arose after the copies began preparing the batch.</param>
    /// <param name="applying">Whether the copies were sent the batch to apply.</param>
    internal static void Fail(StorageCopyOutcome[] outcomes, int sent, Exception error, bool persisted, bool started,
        bool applying)
    {
        var transport = error is OperationCanceledException or OrleansException or TimeoutException;
        var unknown = error is KeyLoadException { Code: ErrorCode.UnknownWriteOutcome };
        var replacement = !applying
            ? transport || unknown ? Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable, error) : null
            : unknown ? error
                : persisted || transport ? Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.WriteUncertain, error) : null;
        var failed = replacement is not null ? applying ? sent : outcomes.Length
            : started ? StorageCopyContract.MinimumCount : outcomes.Length;
        for (var index = StorageCopyContract.FirstOwner; index < failed; index++)
        {
            outcomes[index] = StorageCopyOutcome.Failed(replacement ?? error);
        }
    }

    private static void Differ(StorageCopyOutcome[] outcomes, int from, int applied)
    {
        var failure = Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.DifferentOutcomes);
        for (var index = from; index < applied; index++)
        {
            outcomes[index] = StorageCopyOutcome.Failed(failure);
        }
    }
}
