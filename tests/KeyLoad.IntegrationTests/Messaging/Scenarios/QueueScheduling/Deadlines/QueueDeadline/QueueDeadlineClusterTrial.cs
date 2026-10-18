using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineClusterTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDeadlineClusterProtocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        try
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineClusterSelection(subject));
            try
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                await ExecuteAsync(fixture, subject, lifetime.Token);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, string subject, CancellationToken token)
    {
        var tenant = QueueDeadlineClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, QueueDeadlineClusterProtocol.Database, QueueDeadlineClusterProtocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueDeadlineClusterProtocol.Queue);
        var setup = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
            var identity = await MessagingClusterIdentity.CreateSelectedAsync(fixture, subject, tenant,
                [new(partition.DatabaseId, lane.Queue, QueueDeadlineClusterProtocol.Worker)], [], false, token);
            var now = TimeProvider.System.GetUtcNow();
            var due = now.AddSeconds(QueueDeadlineClusterProtocol.LeadSeconds);
            var original = new CommandRequest(Guid.NewGuid(), partition,
                [new EnqueueMessage(lane.Queue, QueueDeadlineClusterProtocol.Original, QueueDeadlineClusterProtocol.Payload,
                QueueDeadlineClusterProtocol.Headers, NotBefore: due),
             new EnqueueMessage(lane.Queue, QueueDeadlineClusterProtocol.Future, QueueDeadlineClusterProtocol.Payload,
                QueueDeadlineClusterProtocol.Headers, NotBefore: now.AddHours(QueueDeadlineClusterProtocol.FutureHours))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(original, token));
            var scheduled = new MessageInspection(new(QueueDeadlineClusterProtocol.Original, MessageState.Scheduled,
                (int)QueueDeadlineClusterProtocol.Initial, QueueDeadlineClusterProtocol.First, QueueDeadlineClusterProtocol.Initial, due, null),
                QueueDeadlineClusterProtocol.Payload, QueueDeadlineClusterProtocol.Headers);
            var future = scheduled with
            {
                Metadata = scheduled.Metadata with
                {
                    Id = QueueDeadlineClusterProtocol.Future,
                    NotBefore = ((EnqueueMessage)original.Mutations.Last()).NotBefore
                }
            };
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, scheduled, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, future, token);
            await Assert.That(TimeProvider.System.GetUtcNow() < due).IsTrue();
            return (Identity: identity, Original: original, Receipt: receipt, Scheduled: scheduled, Future: future);
        }, token);
        await QueueDeadlineClusterContinuation.RunAsync(fixture, lane, setup.Identity, setup.Original, setup.Receipt,
            setup.Scheduled, setup.Future, token);
    }
}
