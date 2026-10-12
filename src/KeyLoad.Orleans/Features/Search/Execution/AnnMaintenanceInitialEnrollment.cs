namespace KeyLoad.Orleans;

internal static class AnnMaintenanceInitialEnrollment
{
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
        if (configured.InitialAnnMaintenance is { } original
            && NativeSerialization.Serialize(original).AsSpan().SequenceEqual(NativeSerialization.Serialize(request)))
        { return original; }
        return null;
    }
}
