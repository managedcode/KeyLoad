using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

/// <summary>AC-CQ-034: native signed claim admission retains real leases and canonical claim bytes.</summary>
internal sealed class SignedClaimsExecutionPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LowerConfiguredAdmissionPreservesLeaseThenExactOrDefaultPolicyAcknowledgesAsync(bool useDefault)
    {
        using var database = new TestDatabase();
        var delivery = SignedClaimsExecutionPolicyFlow.Lease(database);
        var lane = SignedClaimsExecutionPolicyFlow.Lane(database);
        var before = SignedClaimsExecutionPolicyFlow.CaptureLease(database, delivery);
        var inspection = database.Database.InspectMessage(SignedClaimsExecutionPolicyFlow.PrincipalId, lane, delivery.Id)!;
        var shortOptions = UnitExecutionOptions.NativeClaimsExecution(new() { MaximumTokenCharacters = delivery.Token.Length - 1 });
        var lower = SignedClaimsExecutionPolicyFlow.Borrow(database, shortOptions);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => lower.Verify<DeliveryClaims>(delivery.Token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(SignedClaimsExecutionPolicyFlow.Acknowledge(lower, lane, delivery).Error)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        var after = SignedClaimsExecutionPolicyFlow.CaptureLease(database, delivery);
        for (var index = 0; index < before.Length; index++)
        {
            await Assert.That(after[index].SequenceEqual(before[index])).IsTrue();
        }
        await Assert.That(database.Database.InspectMessage(SignedClaimsExecutionPolicyFlow.PrincipalId, lane, delivery.Id))
            .IsEqualTo(inspection);
        await Assert.That(inspection.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(inspection.PayloadJson).IsEqualTo(SignedClaimsExecutionPolicyFlow.Payload);
        await Assert.That(inspection.HeadersJson).IsEqualTo(SignedClaimsExecutionPolicyFlow.Headers);

        var healthyOptions = UnitExecutionOptions.NativeClaimsExecution(useDefault ? null
            : new NativeClaimsExecutionOptions { MaximumTokenCharacters = delivery.Token.Length });
        var healthy = SignedClaimsExecutionPolicyFlow.Borrow(database, healthyOptions);
        await Assert.That(healthy.Store).IsSameReferenceAs(database.Store);
        await Assert.That(healthy.ClaimsExecution).IsSameReferenceAs(healthyOptions.Value);
        var claims = new DeliveryClaims(lane, delivery.Id, SignedClaimsExecutionPolicyFlow.PrincipalId,
            delivery.LeaseVersion, delivery.DeliveryGeneration, database.Store.Identity.Incarnation);
        await Assert.That(healthy.Verify<DeliveryClaims>(delivery.Token)).IsEqualTo(claims);
        await Assert.That(healthy.Sign(claims)).IsEqualTo(delivery.Token);
        SignedClaimsExecutionPolicyFlow.Acknowledge(healthy, lane, delivery).Get<CommitReceipt>();
        await AssertAcknowledgedAsync(database, healthy, lane, delivery);
    }

    private static async Task AssertAcknowledgedAsync(TestDatabase database, DatabaseEngine healthy,
        QueueLaneRef lane, Delivery delivery)
    {
        var completed = healthy.InspectMessage(SignedClaimsExecutionPolicyFlow.PrincipalId, lane, delivery.Id)!;
        await Assert.That(completed.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(completed.Metadata.LeaseOwner).IsNull();
        await Assert.That(completed.Metadata.LeaseUntil).IsNull();
        await Assert.That(completed.PayloadJson).IsNull();
        await Assert.That(completed.HeadersJson).IsNull();
        var counters = SignedClaimsExecutionPolicyFlow.Counters(database);
        await Assert.That(counters.StoredMessages).IsEqualTo(0L);
        await Assert.That(counters.StoredBytes).IsEqualTo(0L);
        await Assert.That(counters.InFlightMessages).IsEqualTo(0L);
        await Assert.That(counters.InFlightBytes).IsEqualTo(0L);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            SignedClaimsExecutionPolicyFlow.LeaseKey(database, delivery)))).IsNull();
    }

    [Test]
    public async Task ExplicitOperandKeepsNativeClaimsContractUnderLowerImplicitAdmissionAsync()
    {
        using var database = new TestDatabase();
        var delivery = SignedClaimsExecutionPolicyFlow.Lease(database);
        var lower = SignedClaimsExecutionPolicyFlow.Borrow(database,
            UnitExecutionOptions.NativeClaimsExecution(new() { MaximumTokenCharacters = 1 }));
        var expected = database.Database.Verify<DeliveryClaims>(delivery.Token);
        await Assert.That(lower.Verify<DeliveryClaims>(delivery.Token, delivery.Token.Length)).IsEqualTo(expected);
        await Assert.That(lower.Sign(expected)).IsEqualTo(delivery.Token);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => lower.Verify<DeliveryClaims>(delivery.Token)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            lower.Verify<DeliveryClaims>(delivery.Token, delivery.Token.Length - 1)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(8193)]
    public async Task InvalidAdmissionRejectsBeforePhysicalStoreCreationAsync(int maximumCharacters)
    {
        var directory = Path.Combine(Path.GetTempPath(), "signed-claims-invalid-" + Guid.NewGuid().ToString("N"));
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var database = new TestDatabase(directory: directory,
                claimsExecution: new() { MaximumTokenCharacters = maximumCharacters });
        });
        await Assert.That(failure.Message).IsEqualTo(NativeClaimsExecutionOptions.ValidationMessage);
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    public async Task CanonicalDefaultAndInclusivePolicyBoundariesAreValidatedAsync()
    {
        await Assert.That(UnitExecutionOptions.NativeClaimsExecution().Value.MaximumTokenCharacters).IsEqualTo(8192);
        foreach (var maximumCharacters in new[] { 1, 8192 })
        {
            var options = UnitExecutionOptions.NativeClaimsExecution(new() { MaximumTokenCharacters = maximumCharacters });
            await Assert.That(options.Value.IsValid()).IsTrue();
            await Assert.That(options.Value.MaximumTokenCharacters).IsEqualTo(maximumCharacters);
        }
    }
}
