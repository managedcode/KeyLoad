namespace KeyLoad.Orleans;

/// <summary>Borrowed node-local text owner; every invocation is an independently signed request capability.</summary>
internal interface INativeTextMaintenance
{
    Task<TextMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, CancellationToken cancellationToken);

    Task AbortAsync(Guid sessionId);
}
