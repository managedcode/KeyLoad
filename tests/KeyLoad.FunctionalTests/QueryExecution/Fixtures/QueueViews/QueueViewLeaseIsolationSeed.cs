namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationSeed
{
    internal static async Task RunAsync(QueueViewLeaseIsolationState state)
    {
        foreach (var (name, kind) in new[] { (QueueViewLeaseIsolationProtocol.Events, ResourceKind.StreamSet),
            (QueueViewLeaseIsolationProtocol.Queue, ResourceKind.WorkQueue) })
        {
            var resource = new ResourceDefinition(name, kind, state.Partition.TransactionDomainId)
            {
                FieldPolicies = [new(QueueViewLeaseIsolationProtocol.PayloadPath, QueueViewLeaseIsolationProtocol.Classification,
                    QueueViewLeaseIsolationProtocol.ReadGrant, QueueViewLeaseIsolationProtocol.UseGrant)],
                HeaderPolicies = [new(QueueViewLeaseIsolationProtocol.HeaderPath, QueueViewLeaseIsolationProtocol.Classification,
                    QueueViewLeaseIsolationProtocol.ReadGrant, QueueViewLeaseIsolationProtocol.UseGrant)]
            };
            await QueueViewLeaseIsolationOperations.SendAsync(state, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, resource), Guid.NewGuid(), resource);
        }
        await QueueViewLeaseIsolationPolicy.SetAsync(state, true, QueueViewLeaseIsolationProtocol.Initial);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, state.Partition,
        [new AppendEvents(QueueViewLeaseIsolationProtocol.Events, QueueViewLeaseIsolationProtocol.Stream,
            [new(QueueViewLeaseIsolationProtocol.FirstEvent, QueueViewLeaseIsolationProtocol.CreatedType,
                QueueViewLeaseIsolationProtocol.FirstPayload, QueueViewLeaseIsolationProtocol.FirstHeaders),
             new(QueueViewLeaseIsolationProtocol.SecondEvent, QueueViewLeaseIsolationProtocol.PaidType,
                QueueViewLeaseIsolationProtocol.SecondPayload, QueueViewLeaseIsolationProtocol.SecondHeaders)], ExpectedStreamRevision.NoStream),
         new EnqueueMessage(QueueViewLeaseIsolationProtocol.Queue, QueueViewLeaseIsolationProtocol.First,
            QueueViewLeaseIsolationProtocol.FirstPayload, QueueViewLeaseIsolationProtocol.FirstHeaders, QueueViewLeaseIsolationProtocol.NotBefore),
         new EnqueueMessage(QueueViewLeaseIsolationProtocol.Queue, QueueViewLeaseIsolationProtocol.Second,
            QueueViewLeaseIsolationProtocol.SecondPayload, QueueViewLeaseIsolationProtocol.SecondHeaders, QueueViewLeaseIsolationProtocol.NotBefore)]);
        var expected = QueueViewLeaseIsolationOperations.Receipt(state, id,
        [new(QueueViewLeaseIsolationProtocol.AppendKind, QueueViewLeaseIsolationProtocol.Events,
            QueueViewLeaseIsolationProtocol.Stream, QueueViewLeaseIsolationProtocol.Leased),
         new(QueueViewLeaseIsolationProtocol.EnqueueKind, QueueViewLeaseIsolationProtocol.Queue,
            QueueViewLeaseIsolationProtocol.First, QueueViewLeaseIsolationProtocol.Initial),
         new(QueueViewLeaseIsolationProtocol.EnqueueKind, QueueViewLeaseIsolationProtocol.Queue,
            QueueViewLeaseIsolationProtocol.Second, QueueViewLeaseIsolationProtocol.Initial)]);
        await QueueViewLeaseIsolationOperations.SendAsync(state, OperationKind.Batch, command, id, expected);
    }
}
