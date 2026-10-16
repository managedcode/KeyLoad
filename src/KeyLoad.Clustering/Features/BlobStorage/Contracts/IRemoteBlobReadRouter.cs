using KeyLoad.Core.Features.ResourceExecution;
namespace KeyLoad.Clustering;

/// <summary>Configured physical-owner transport called by the existing authorized Blob read actor.</summary>
internal interface IRemoteBlobReadRouter
{
    /// <summary>Reads the original native Blob request using fresh canonical policy and current physical lifetime.</summary>
    /// <param name="envelope">Original verified ingress envelope.</param>
    /// <param name="principal">Fresh persisted canonical subject.</param>
    /// <param name="payload">Original native request bytes.</param>
    /// <param name="cancellationToken">Original request cancellation.</param>
    /// <param name="binding">The actual original hosted lifetime binding and result ledger.</param>
    /// <returns>The bounded native Blob result after canonical revalidation.</returns>
    Task<object?> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        ReadOnlyMemory<byte> payload, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken);
}
