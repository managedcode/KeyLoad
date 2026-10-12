namespace KeyLoad.UnitTests;

internal sealed class ReplicaSubmissionOutcomeTests
{
    [Test]
    public async Task SuccessfulOriginalStrictResultAndSameIdReplayPreserveFullNativeState()
    {
        using var flow = new ReplicaSubmissionOutcomeFlow();
        var operation = flow.Create("original", "{\"value\":1}");
        var result = flow.Submit(operation);
        await Assert.That(result.Error).IsNull();
        var state = ReplicaSubmissionOutcomeState.Read(flow.Database);
        var document = flow.Document("original");
        var entry = NativeSerialization.Serialize(flow.Database.ReadRetainedEntry(operation.Id));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, flow.Database.Database.ResolveSubmissionOutcome(operation));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, flow.Database.Database.ResolveOutcome(operation));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, flow.Submit(operation));
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(document, flow.Document("original"));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(entry, NativeSerialization.Serialize(flow.Database.ReadRetainedEntry(operation.Id)));
        var following = flow.Create("following", "{\"value\":2}");
        await Assert.That(flow.Submit(following).Error).IsNull();
        await Assert.That(flow.Database.Database.ResolveSubmissionOutcome(following).Error).IsNull();
    }

    [Test]
    public async Task MissingOriginalThrowsStrictlyWhilePublicReaderRetainsItsFailureResult()
    {
        using var flow = new ReplicaSubmissionOutcomeFlow();
        var operation = flow.Create("missing", "{\"value\":3}");
        var state = ReplicaSubmissionOutcomeState.Read(flow.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.ResolveSubmissionOutcome(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(failure.Message).IsEqualTo("The applied command has no durable outcome.");
        var publicResult = flow.Database.Database.ResolveOutcome(operation);
        await Assert.That(publicResult.Error).IsEqualTo(failure.Code);
        await Assert.That(publicResult.SafeDetail).IsEqualTo(failure.Message);
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
        var result = flow.Submit(operation);
        await Assert.That(result.Error).IsNull();
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, flow.Database.Database.ResolveSubmissionOutcome(operation));
    }

    [Test]
    public async Task CurrentPersistedPolicyChangeIsObservationFailureNotStoredTerminalRefusal()
    {
        using var flow = new ReplicaSubmissionOutcomeFlow();
        var operation = flow.Create("policy", "{\"value\":4}");
        await Assert.That(flow.Submit(operation).Error).IsNull();
        var originalOutcome = flow.Outcome(operation);
        var document = flow.Document("policy");
        var changed = flow.Principal with { PolicyEpoch = checked(flow.Principal.PolicyEpoch + 1) };
        var configured = flow.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(changed)).Get<PrincipalRecord>();
        await Assert.That(configured.PolicyEpoch).IsEqualTo(changed.PolicyEpoch);
        var state = ReplicaSubmissionOutcomeState.Read(flow.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.ResolveSubmissionOutcome(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Message).IsEqualTo("The principal policy changed since this command was evaluated.");
        var publicResult = flow.Database.Database.ResolveOutcome(operation);
        await Assert.That(publicResult.Error).IsEqualTo(failure.Code);
        await Assert.That(publicResult.SafeDetail).IsEqualTo(failure.Message);
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(originalOutcome, flow.Outcome(operation));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(document, flow.Document("policy"));
        var following = flow.Create("fresh-policy", "{\"value\":5}");
        await Assert.That(flow.Submit(following).Error).IsNull();
        await Assert.That(flow.Database.Database.ResolveSubmissionOutcome(following).Error).IsNull();
    }

    [Test]
    public async Task ActualStoredRevisionConflictRetainsExactFailureAndReplayWithoutEffects()
    {
        using var flow = new ReplicaSubmissionOutcomeFlow();
        await Assert.That(flow.Submit(flow.Create("conflict", "{\"value\":6}")).Error).IsNull();
        var document = flow.Document("conflict");
        var operation = flow.Create("conflict", "{\"value\":7}", expectedRevision: 0);
        var original = flow.Submit(operation);
        await Assert.That(original.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(original.SafeDetail).IsEqualTo("The expected revision does not match.");
        var state = ReplicaSubmissionOutcomeState.Read(flow.Database);
        var outcome = flow.Outcome(operation);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original, flow.Database.Database.ResolveSubmissionOutcome(operation));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original, flow.Submit(operation));
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(outcome, flow.Outcome(operation));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(document, flow.Document("conflict"));
        var following = flow.Create("conflict", "{\"value\":8}", expectedRevision: 1);
        await Assert.That(flow.Submit(following).Error).IsNull();
        await Assert.That(flow.Database.Database.ResolveSubmissionOutcome(following).Error).IsNull();
    }
}
