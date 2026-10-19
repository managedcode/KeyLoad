namespace KeyLoad.Storage;

/// <summary>Validates proposed mutations together with the actual staged commit without staging or durable effects.</summary>
public interface IAtomicCommitCandidateValidation
{
    /// <summary>Uses the owning provider's execution commit encoding and unchanged frame limit.</summary>
    /// <param name="mutations">Bounded proposed logical mutations, applied in order over the existing staged view.</param>
    void ValidateCandidate(IReadOnlyList<StorageMutation> mutations);
}
