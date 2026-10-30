namespace KeyLoad.Clustering;

/// <summary>One independent, server-addressed execution context for one admitted request.</summary>
[Orleans.Alias(GrainRoutingProtocol.ExecutionAlias), Orleans.CodeGeneration.Version(GrainRoutingProtocol.ExecutionInterfaceVersion)]
public interface IRequestExecutionGrain : Orleans.IGrainWithGuidKey
{
    /// <summary>Executes beside the selected physical owner without taking ownership of its resources.</summary>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteAlias)]
    Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, GrainRequestEnvelope envelope, string? actorKey,
        CancellationToken cancellationToken);
}
