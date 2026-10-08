namespace KeyLoad.Orleans;

// Invoked only by the existing separately keyed DatabaseReadGrain after quorum and persisted admin reload.
internal interface INativeAnnMaintenance
{
    Task AbortAsync(Guid sessionId);

    Task<AnnMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken cancellationToken);
}
