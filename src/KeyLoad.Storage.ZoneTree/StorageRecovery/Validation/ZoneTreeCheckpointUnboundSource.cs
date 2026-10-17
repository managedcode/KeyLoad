namespace KeyLoad.Storage.ZoneTree;

/// <summary>Raw internal writers can only emit zero-authority images without an actual source view.</summary>
internal static class ZoneTreeCheckpointUnboundSource
{
    internal static void Observe(ZoneTreeCheckpointSourceProof? proof, ZoneTreeConservationBudget budget, ZoneTreeStoreOptions options,
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        budget.Charge(checked((long)key.Length + value.Length));
        if (proof is not null)
        {
            proof.Observe(key, value);
            return;
        }
        if (ResourceRetirementAuthorityClassifier.TryRead(key, value, options.MaxFrameBytes, out _)
            || key.SequenceEqual(ResourceRetirementConservationKeys.Floor())
            || key.StartsWith(ResourceRetirementConservationKeys.StepPrefix())
            || key.StartsWith(ResourceRetirementConservationKeys.ReleasePrefix()))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        }
    }
}
