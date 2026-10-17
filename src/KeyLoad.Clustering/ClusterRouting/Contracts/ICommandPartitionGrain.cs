namespace KeyLoad.Clustering;

/// <summary>Command routing actor keyed by canonical atomic partition or an administrative catalog.</summary>
[Orleans.Alias(GrainRoutingProtocol.CommandAlias), Orleans.CodeGeneration.Version(GrainRoutingProtocol.CapabilityInterfaceVersion)]
public interface ICommandPartitionGrain : Orleans.IGrainWithStringKey
{
    /// <summary>Revalidate authority and submit the stable command to the node-owned consensus host.</summary>
    /// <param name="signedRequest">Signed request routed to this exact partition key.</param>
    /// <param name="cancellationToken">Bounded caller cancellation.</param>
    /// <returns>Committed result or typed safe rejection, preserving unknown-write outcomes.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteAlias)]
    Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken);

    /// <summary>Prepares the original authenticated command and returns its bounded target-owned claim without effects.</summary>
    /// <param name="signedRequest">The original signed command and finite observation admission.</param>
    /// <param name="cancellationToken">Original preparation cancellation.</param>
    /// <returns>The opaque original-owner proof; no command has been submitted.</returns>
    [Orleans.Alias(NativeCommandObservationPreparation.Alias)]
    Task<GrainOperationReply> PrepareOriginalCommandAsync(string signedRequest, CancellationToken cancellationToken);
}
