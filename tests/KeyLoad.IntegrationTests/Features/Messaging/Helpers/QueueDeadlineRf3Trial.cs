using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Trial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDeadlineRf3Protocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        try
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineRf3Selection(subject));
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
        var tenant = QueueDeadlineRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, QueueDeadlineRf3Protocol.Database, QueueDeadlineRf3Protocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueDeadlineRf3Protocol.Queue);
        var setup = await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
            var identity = await MessagingRf3Identity.CreateSelectedAsync(fixture, subject, tenant,
                [new(partition.DatabaseId, lane.Queue, QueueDeadlineRf3Protocol.Worker)], [], false, token);
            var now = TimeProvider.System.GetUtcNow();
            var due = now.AddSeconds(QueueDeadlineRf3Protocol.LeadSeconds);
            var original = new CommandRequest(Guid.NewGuid(), partition,
                [new EnqueueMessage(lane.Queue, QueueDeadlineRf3Protocol.Original, QueueDeadlineRf3Protocol.Payload,
                QueueDeadlineRf3Protocol.Headers, NotBefore: due),
             new EnqueueMessage(lane.Queue, QueueDeadlineRf3Protocol.Future, QueueDeadlineRf3Protocol.Payload,
                QueueDeadlineRf3Protocol.Headers, NotBefore: now.AddHours(QueueDeadlineRf3Protocol.FutureHours))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(original, token));
            var scheduled = new MessageInspection(new(QueueDeadlineRf3Protocol.Original, MessageState.Scheduled,
                (int)QueueDeadlineRf3Protocol.Initial, QueueDeadlineRf3Protocol.First, QueueDeadlineRf3Protocol.Initial, due, null),
                QueueDeadlineRf3Protocol.Payload, QueueDeadlineRf3Protocol.Headers);
            var future = scheduled with
            {
                Metadata = scheduled.Metadata with
                { Id = QueueDeadlineRf3Protocol.Future, NotBefore = ((EnqueueMessage)original.Mutations.Last()).NotBefore }
            };
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, scheduled, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            await Assert.That(TimeProvider.System.GetUtcNow() < due).IsTrue();
            return (Identity: identity, Original: original, Receipt: receipt, Scheduled: scheduled, Future: future);
        }, token);
        await QueueDeadlineRf3Continuation.RunAsync(fixture, lane, setup.Identity, setup.Original, setup.Receipt,
            setup.Scheduled, setup.Future, token);
    }
}
