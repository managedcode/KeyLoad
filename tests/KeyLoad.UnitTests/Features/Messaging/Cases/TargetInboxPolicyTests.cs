namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class TargetInboxPolicyTests
{
    [Test]
    public async Task UnconfiguredTargetInboxRefusesWithoutEffectsThenExplicitConfiguredTargetCompletes()
    {
        using var fixture = new TestDatabase();
        var seed = TargetInboxNativeSetup.Create(fixture);
        var token = TestContext.Current!.Execution.CancellationToken;
        var unsupported = seed.Request with { CommandId = Guid.NewGuid(), Target = new(seed.Target.Partition, TargetInboxUnitProtocol.Output) };
        await TargetInboxNativeAssertions.RefusedAsync(fixture.Database, fixture.Store, unsupported, ErrorCode.UnsupportedCapability, token);
        var completed = TargetInboxNativeSetup.Apply(fixture.Database, seed.Request, token).Get<CommitInboxResult>();
        await Assert.That(completed.AlreadyProcessed).IsFalse();
        await TargetInboxNativeAssertions.EffectsAsync(fixture.Database, seed.Request);
        await TargetInboxNativeAssertions.CapacityAsync(fixture.Store, seed.Request, TargetInboxUnitProtocol.FirstRevision);
    }
}
