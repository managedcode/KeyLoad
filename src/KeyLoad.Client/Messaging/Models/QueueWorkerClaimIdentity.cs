namespace KeyLoad.Client;

/// <summary>Bounded claim identity history that does not retain completed body/header strings.</summary>
/// <param name="Id">Actual message identity.</param>
/// <param name="Token">Actual signed token returned by the server.</param>
/// <param name="LeaseVersion">Actual lease version.</param>
/// <param name="LeaseUntil">Actual original deadline; local clocks do not authorize ACK.</param>
/// <param name="Attempt">Actual claim attempt.</param>
/// <param name="DeliveryGeneration">Actual delivery generation.</param>
public sealed record QueueWorkerClaimIdentity(string Id, string Token, long LeaseVersion,
    DateTimeOffset LeaseUntil, int Attempt, long DeliveryGeneration);
