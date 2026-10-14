namespace KeyLoad.Orleans;

internal static class AnnMaintenanceInitialEnrollment
{
    private const long InitialSequence = 0;

    internal static async Task<AnnMaintenanceRequest?> ReadAsync(AnnMaintenanceChildCalls children,
        AnnMaintenanceRequest request, CancellationToken token)
    {
        if (request.Mode != AnnMaintenanceMode.Build || request.MaintenanceProfile is null)
        { return null; }
        var actual = await children.ReadAsync<GetOutboxStatusRequest, OutboxStatus>(GrainReadKind.OutboxStatus,
            new(request.Consumer.Partition), token).ConfigureAwait(true);
        var configured = actual.Consumers.SingleOrDefault(value => value.Consumer == request.Consumer);
        if (configured is null)
        { return request; }
        var initial = configured.InitialAnnMaintenance is { } original
            && global::KeyLoad.AnnMaintenanceRequestIdentity.Same(original, request)
            ? original : null;
        var configureId = AnnMaintenanceChildIdentity.Create(request.CommandId,
            AnnMaintenancePhase.Configure, InitialSequence);
        global::KeyLoad.Core.DatabaseEngine.RequireInitialMaintenanceReservation(configured,
            new ConfigureProjectionConsumerRequest(configureId, request.Consumer, configured.Definition)
            { InitialAnnMaintenance = initial });
        return initial;
    }
}
