namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    /// <summary>Issue a bounded close control for a server-owned connection.</summary>
    /// <param name="connectionId">The server-issued execution owner identity.</param>
    /// <returns>The signed, expiring control.</returns>
    public string CreateConnectionClose(Guid connectionId)
        => ConnectionControlCodec.Create(database, clock, settings, connectionId);

    /// <summary>Verify a close control against the current connection and database.</summary>
    /// <param name="signedControl">The server-issued bounded token.</param>
    /// <param name="connectionId">The actual grain owner identity.</param>
    public void VerifyConnectionClose(string signedControl, Guid connectionId)
        => ConnectionControlCodec.Verify(database, clock, settings, signedControl, connectionId);
}
