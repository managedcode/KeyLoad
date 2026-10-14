using KeyLoad.Server.Features.BlobStorage;

namespace KeyLoad.UnitTests;

internal sealed class NativeBlobWireReservationCases
{
    [Test]
    public Task FourActualReservationsRefuseTheFifthAndExactReleaseReusesOnlyItsSlot()
        => NativeBlobWireFileFixture.RunOwnedAsync(async fixture =>
        {
            var original = new List<NativeBlobWireReservation>();
            foreach (var identity in Enum.GetValues<NativeBlobWireIdentity>().Take(4))
            {
                var selected = fixture.PublishClaim(identity);
                original.Add(fixture.Reservations.Enter(fixture.Owner, fixture.OwnerDigest, selected.Claim, selected.Digest));
            }
            await Assert.That(original.Select(value => value.Slot).SequenceEqual([0, 1, 2, 3])).IsTrue();
            var next = fixture.PublishClaim(NativeBlobWireIdentity.Delete);
            var failed = Assert.ThrowsExactly<InvalidOperationException>(() =>
                fixture.Reservations.Enter(fixture.Owner, fixture.OwnerDigest, next.Claim, next.Digest));
            await Assert.That(failed.Message).IsEqualTo(NativeBlobWireProbeProtocol.Contention);
            var firstBytes = fixture.Files.Read(NativeBlobWireNames.Slot(0));
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Reservations.Leave(original[0] with { CallbackId = Guid.NewGuid() }));
            await Assert.That(fixture.Files.Read(NativeBlobWireNames.Slot(0)).AsSpan().SequenceEqual(firstBytes)).IsTrue();
            fixture.Reservations.Leave(original[0]);
            var acquired = fixture.Reservations.Enter(fixture.Owner, fixture.OwnerDigest, next.Claim, next.Digest);
            await Assert.That(acquired.Slot).IsEqualTo(0);
            fixture.Reservations.Leave(acquired);
            foreach (var reservation in original.Skip(1))
            {
                fixture.Reservations.Leave(reservation);
            }
            for (var slot = 0; slot < 4; slot++)
            {
                await Assert.That(fixture.Files.Exists(NativeBlobWireNames.Slot(slot))).IsFalse();
            }
        });

    [Test]
    public async Task ActualCallbackCloseWaitsForLeaveAndRejectsNewAdmission()
    {
        var lifetime = new NativeBlobWireCallbackLifetime();
        lifetime.Enter();
        var closed = lifetime.CloseAsync();
        try
        {
            await Assert.That(closed.IsCompleted).IsFalse();
            Assert.ThrowsExactly<ObjectDisposedException>(lifetime.Enter);
        }
        finally { lifetime.Leave(); }
        await closed;
        await Assert.That(ReferenceEquals(closed, lifetime.CloseAsync())).IsTrue();
        Assert.ThrowsExactly<InvalidOperationException>(lifetime.Leave);
    }
}
