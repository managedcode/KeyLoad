namespace KeyLoad.Orleans;

/// <summary>Provides a bounded, in-process borrow of an already native-authorized parent Retire operation.</summary>
/// <remarks>This observer conveys no execution permission and has no serialized or public transport contract.</remarks>
public interface IGrainPartitionMovementSealedOperationObserver
{
    /// <summary>Borrows the immutable original operation under its unchanged producer cancellation.</summary>
    /// <param name="operation">The exact operation verified by the native command owner.</param>
    /// <param name="cancellationToken">Original command lifetime; cancellation must settle the borrow.</param>
    /// <returns>Completion after the observer has released the producer.</returns>
    ValueTask BorrowAsync(ReplicatedOperation operation, CancellationToken cancellationToken);
}
