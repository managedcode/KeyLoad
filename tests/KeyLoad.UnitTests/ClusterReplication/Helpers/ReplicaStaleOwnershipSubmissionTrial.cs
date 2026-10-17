namespace KeyLoad.UnitTests;

internal static class ReplicaStaleOwnershipSubmissionTrial
{
    internal static async Task RunAsync()
    {
        await ReplicaSubmissionOutcomeFlow.RunAsync(async flow =>
        {
            await Assert.That(flow.Submit(flow.Create("stale-owner", "{\"value\":1}")).Error).IsNull();
            var originalDocument = flow.Document("stale-owner");
            var placement = flow.Database.Store.Read(view =>
                global::KeyLoad.Core.DatabaseEngine.ReadPlacementWitness(view, flow.Database.Partition));
            var id = Guid.NewGuid();
            var command = new CommandRequest(id, flow.Database.Partition,
                [new PutDocument(ReplicaSubmissionOutcomeFlow.Resource, "stale-owner", "{\"value\":2}", 1)],
                checked(placement.PlacementEpoch + 1));
            var operation = flow.Database.Database.CreateNativeOperation(OperationKind.Batch, id,
                ReplicaSubmissionOutcomeFlow.Writer, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var candidate = flow.Database.Database.TryReadOrdinaryDocumentSubmission(operation)
                ?? throw new InvalidOperationException("The genuine document command was not eligible.");
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(command),
                NativeSerialization.Serialize(candidate));
            var unrelated = flow.Database.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(),
                ReplicaSubmissionOutcomeFlow.Writer, TimeProvider.System.GetUtcNow(),
                NativeSerialization.Serialize(new ConfigurePrincipalRequest(flow.Principal)));
            await Assert.That(flow.Database.Database.TryReadOrdinaryDocumentSubmission(unrelated)).IsNull();
            flow.Database.Database.ValidateSubmissionAdmission(unrelated);
            flow.Database.Database.ValidateSubmissionAdmission(operation);
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            var original = flow.Submit(operation);
            await Assert.That(original.Error).IsEqualTo(ErrorCode.OwnershipLost);
            var outcome = flow.Outcome(operation);
            var stored = flow.Database.Store.Read(view =>
                KeyLoad.Core.Features.ClusterRouting.Execution.CommandOutcomeKeyResolver.Select(view,
                    operation.PrincipalId, operation.Id,
                    KeyLoad.Core.Features.ClusterRouting.Identity.CommandOutcomePartitionIdentity.Resolve(operation)).Outcome
                    ?? throw new InvalidOperationException("The genuine stale-owner failure was not persisted."));
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original, stored.Result);
            await Assert.That(stored.PolicyEpoch).IsEqualTo(flow.Principal.PolicyEpoch);
            var failed = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var strict = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.ResolveSubmissionOutcome(operation));
            await Assert.That(strict.Code).IsEqualTo(original.Error!.Value);
            await Assert.That(strict.Message).IsEqualTo(original.SafeDetail);
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original, flow.Database.Database.ResolveOutcome(operation));
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original, flow.Submit(operation));
            await failed.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(outcome, flow.Outcome(operation));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(originalDocument, flow.Document("stale-owner"));
            await Assert.That(flow.Submit(flow.Create("stale-owner", "{\"value\":3}", expectedRevision: 1)).Error).IsNull();
        });
    }
}
