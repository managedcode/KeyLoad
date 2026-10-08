namespace KeyLoad.Orleans;

/// <summary>Owns opt-in configured physical-owner execution of the existing public partition query.</summary>
public interface IRemotePartitionQueryRouter
{
    /// <summary>Runs uniquely signed leaves under the original parent scope, cancellation and bounded plan.</summary>
    /// <param name="envelope">Original verified native parent identity and expiry.</param>
    /// <param name="principal">Fresh source persisted subject, never forwarded grants.</param>
    /// <param name="request">Existing public bounded partition-query request.</param>
    /// <param name="cancellationToken">Original capability token.</param>
    /// <returns>Only a fully validated complete page after every child joins.</returns>
    Task<PartitionQueryPageV1> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        PartitionQueryRequestV1 request, CancellationToken cancellationToken);
}
