namespace KeyLoad.Core;

/// <summary>Owns the original read grant for one verified source-Admit journal inspection.</summary>
public sealed class EventVectorSourceAdmitProbe : IDisposable
{
    private readonly EventVectorFirstAdmitProbe ownedGrant;

    internal EventVectorSourceAdmitProbe(EventVectorFirstAdmitProbe ownedGrant)
    { this.ownedGrant = ownedGrant; }

    /// <summary>Gets the same-view canonical applied position.</summary>
    public long AppliedPosition { get; internal set; }
    /// <summary>Gets the original authenticated parent request ID.</summary>
    public Guid OriginalParentRequestId { get; internal set; }
    /// <summary>Gets the exact intended original source phase command ID.</summary>
    public Guid OriginalSourcePhaseCommandId { get; internal set; }
    /// <summary>Gets a genuine current-authorized existing outcome, if present.</summary>
    public OperationResult? OriginalOutcome { get; internal set; }

    /// <summary>Checks the original cancellation and phase expiry.</summary>
    public void Check() => ownedGrant.Check();
    /// <summary>Charges the native observer's actual key/value bytes before copy/decode.</summary>
    /// <param name="encodedBytes">Actual encoded bytes, including an examined missing lookup.</param>
    public void AcceptNativeRecord(long encodedBytes) => ownedGrant.AcceptNativeRecord(encodedBytes);
    internal void DisposeAfterFailure(Exception primary) => ownedGrant.DisposeAfterFailure(primary);
    /// <inheritdoc />
    public void Dispose() => ownedGrant.Dispose();
}
