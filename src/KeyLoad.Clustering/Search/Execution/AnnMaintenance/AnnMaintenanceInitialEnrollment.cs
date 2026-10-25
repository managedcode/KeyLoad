namespace KeyLoad.Clustering;

internal static class AnnMaintenanceInitialEnrollment
{
    private const long InitialSequence = 0;

    internal static async Task<(bool HasValue, AnnMaintenanceRequest? Value)> ReadAsync(AnnMaintenanceChildCalls children,
        AnnMaintenanceRequest request, CancellationToken token)
    {
        if (request.Mode != AnnMaintenanceMode.Build || request.MaintenanceProfile is null)
        {
            return (false, default(AnnMaintenanceRequest?));
        }
        var actual = await children.ReadAsync<GetOutboxStatusRequest, OutboxStatus>(GrainReadKind.OutboxStatus,
            new(request.Consumer.Partition), token).ConfigureAwait(true);
        var configured = actual.Consumers.SingleOrDefault(value => value.Consumer == request.Consumer);
        if (configured is null)
        {
            return (true, ((AnnMaintenanceRequest?)(request)));
        }
        var initial = configured.InitialAnnMaintenance is { } original
            && global::KeyLoad.AnnMaintenanceRequestIdentity.Same(original, request)
            ? original : null;
        var configureId = AnnMaintenanceChildIdentity.Create(request.CommandId,
            AnnMaintenancePhase.Configure, InitialSequence);
        global::KeyLoad.Core.ProjectionMaintenanceConfiguration.RequireInitialMaintenanceReservation(configured,
            new ConfigureProjectionConsumerRequest(configureId, request.Consumer, configured.Definition)
            {
                InitialAnnMaintenance = initial
            });
        return ((AnnMaintenanceRequest?)(initial)) is { } outcomeValue3 ? (true, outcomeValue3) : (false, default(AnnMaintenanceRequest?));
    }
}
