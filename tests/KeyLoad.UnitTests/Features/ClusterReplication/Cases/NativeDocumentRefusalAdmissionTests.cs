namespace KeyLoad.UnitTests;

internal sealed class NativeDocumentRefusalAdmissionTests
{
    [Test]
    public async Task ChangedDeniedIdentityKeepsCurrentRefusalAndCompleteOriginalState()
    {
        await ReplicaSubmissionOutcomeFlow.RunAsync(async flow =>
        {
            await Assert.That(flow.Submit(flow.Create("retained-denial", "{\"value\":1}")).Error).IsNull();
            var document = flow.Document("retained-denial");
            var denied = flow.Principal with { Grants = [], PolicyEpoch = checked(flow.Principal.PolicyEpoch + 1) };
            flow.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(denied));
            var original = flow.Create("retained-denial", "{\"value\":2}", expectedRevision: 1);
            flow.Database.Database.ValidateSubmissionAdmission(original);
            var refusal = flow.Submit(original);
            await Assert.That(refusal.Error).IsEqualTo(ErrorCode.PermissionDenied);
            var outcome = flow.Outcome(original);
            var entry = NativeSerialization.Serialize(flow.Database.ReadRetainedEntry(original.Id));
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            flow.Database.Database.ValidateSubmissionAdmission(original);
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(refusal, flow.Submit(original));
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            var changed = Changed(flow, original, "retained-denial", "{\"value\":3}", 1);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                flow.Database.Database.ValidateSubmissionAdmission(changed));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(failure.Message).IsEqualTo(refusal.SafeDetail);
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(outcome, flow.Outcome(original));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(document, flow.Document("retained-denial"));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(entry,
                NativeSerialization.Serialize(flow.Database.ReadRetainedEntry(original.Id)));
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(refusal,
                flow.Database.Database.ResolveSubmissionOutcome(original));
            var restored = flow.Principal with { PolicyEpoch = checked(denied.PolicyEpoch + 1) };
            flow.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(restored));
            var healthy = flow.Create("retained-denial", "{\"value\":4}", expectedRevision: 1);
            await Assert.That(flow.Submit(healthy).Error).IsNull();
            var restoredState = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var conflict = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.ValidateSubmissionAdmission(
                Changed(flow, healthy, "retained-denial", "{\"value\":5}", 2)));
            await Assert.That(conflict.Code).IsEqualTo(ErrorCode.Conflict);
            await restoredState.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            await Assert.That(flow.Submit(flow.Create("following-denial", "{\"value\":6}")).Error).IsNull();
        });
    }

    [Test]
    public async Task PersistedRevocationAfterIssuanceRefusesBeforeAppendAndRestorationIsHealthy()
    {
        await ReplicaSubmissionOutcomeFlow.RunAsync(async flow =>
        {
            await Assert.That(flow.Submit(flow.Create("revoked-target", "{\"value\":1}")).Error).IsNull();
            var document = flow.Document("revoked-target");
            var issued = flow.Create("revoked-target", "{\"value\":2}", expectedRevision: 1);
            var revoked = flow.Principal with { Revoked = true, PolicyEpoch = checked(flow.Principal.PolicyEpoch + 1) };
            flow.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked));
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                flow.Database.Database.ValidateSubmissionAdmission(issued));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(document, flow.Document("revoked-target"));
            var outcome = flow.Database.Store.Read(view =>
                KeyLoad.Core.Features.ClusterRouting.Execution.CommandOutcomeKeyResolver.Select(view,
                    issued.PrincipalId, issued.Id,
                    KeyLoad.Core.Features.ClusterRouting.Identity.CommandOutcomePartitionIdentity.Resolve(issued)).Outcome);
            await Assert.That(outcome).IsNull();
            var repeated = Assert.ThrowsExactly<KeyLoadException>(() =>
                flow.Database.Database.ValidateSubmissionAdmission(issued));
            await Assert.That(repeated.Code).IsEqualTo(failure.Code);
            await Assert.That(repeated.Message).IsEqualTo(failure.Message);
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            var restored = flow.Principal with { PolicyEpoch = checked(revoked.PolicyEpoch + 1) };
            flow.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(restored));
            var healthy = flow.Create("revoked-target", "{\"value\":3}", expectedRevision: 1);
            flow.Database.Database.ValidateSubmissionAdmission(healthy);
            var result = flow.Submit(healthy);
            await Assert.That(result.Error).IsNull();
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result,
                flow.Database.Database.ResolveSubmissionOutcome(healthy));
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, flow.Submit(healthy));
        });
    }

    private static ReplicatedOperation Changed(ReplicaSubmissionOutcomeFlow flow, ReplicatedOperation original,
        string document, string json, long revision)
        => flow.Database.Database.CreateNativeOperation(OperationKind.Batch, original.Id, original.PrincipalId,
            original.EvaluatedAt, NativeSerialization.Serialize(new CommandRequest(original.Id,
                flow.Database.Partition, [new PutDocument(ReplicaSubmissionOutcomeFlow.Resource, document, json, revision)])));
}
