using System.Security.Claims;
using KeyLoad.Clustering;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class RequestCqrsScopeCases
{
    private const string PersistedPrefix = "scope-subject-";
    private const string UnrelatedKey = "keyload.test.graph-context";
    private const string ProjectedRoleName = "administrator";

    internal static async Task AcCrs006ScopePublishesOnlyPersistedIdentityAndRestoresEveryPriorKey(
        RequestCqrsHostedRuntimeFixture fixture)
    {
        var principal = PersistPrincipal(fixture, PersistedPrefix + Guid.NewGuid().ToString("N"));
        var userSlot = RequestCqrsContextSlot.Capture(OrleansIdentityConstants.USER_CLAIMS);
        var stateSlot = RequestCqrsContextSlot.Capture(GrainRequestStreamProtocol.ContextKey);
        var unrelatedSlot = RequestCqrsContextSlot.Capture(UnrelatedKey);
        try
        {
            foreach (var userPrior in Enum.GetValues<RequestCqrsPriorContextValue>())
            {
                foreach (var statePrior in Enum.GetValues<RequestCqrsPriorContextValue>())
                {
                    await AssertRestorePairAsync(fixture, principal, userSlot, stateSlot,
                        userPrior, statePrior);
                }
            }
        }
        finally
        {
            userSlot.Restore();
            stateSlot.Restore();
            unrelatedSlot.Restore();
        }
    }

    internal static async Task AcCrs006FailedAdmissionLeavesBothRequestContextKeysUntouched(
        RequestCqrsHostedRuntimeFixture fixture)
    {
        var principal = PersistPrincipal(fixture, PersistedPrefix + Guid.NewGuid().ToString("N"));
        var userSlot = RequestCqrsContextSlot.Capture(OrleansIdentityConstants.USER_CLAIMS);
        var stateSlot = RequestCqrsContextSlot.Capture(GrainRequestStreamProtocol.ContextKey);
        try
        {
            var previousUser = new ClaimsPrincipal();
            var previousState = new GrainRequestContextState(Guid.NewGuid(), Guid.NewGuid(), fixture.ConnectionId);
            userSlot.SetPrior(RequestCqrsPriorContextValue.Object, previousUser);
            stateSlot.SetPrior(RequestCqrsPriorContextValue.Object, previousState);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                _ = new GrainRequestIdentityScope(fixture.Services, principal,
                    Guid.Empty, Guid.Empty, CancellationToken.None, connectionId: fixture.ConnectionId));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await userSlot.AssertPriorAsync(RequestCqrsPriorContextValue.Object, previousUser);
            await stateSlot.AssertPriorAsync(RequestCqrsPriorContextValue.Object, previousState);
        }
        finally
        {
            userSlot.Restore();
            stateSlot.Restore();
        }
    }

    internal static async Task AcCrs006AuthenticationPublisherClearsPriorPrincipalAndRestoresIt(
        RequestCqrsHostedRuntimeFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var previousUser = new ClaimsPrincipal();
        var previousState = new GrainRequestContextState(Guid.NewGuid(), Guid.NewGuid(), fixture.ConnectionId);
        var userSlot = RequestCqrsContextSlot.Capture(OrleansIdentityConstants.USER_CLAIMS);
        var stateSlot = RequestCqrsContextSlot.Capture(GrainRequestStreamProtocol.ContextKey);
        try
        {
            userSlot.SetPrior(RequestCqrsPriorContextValue.Object, previousUser);
            stateSlot.SetPrior(RequestCqrsPriorContextValue.Object, previousState);
            using (new GrainRequestIdentityScope(fixture.Services, null,
                requestId, Guid.Empty, CancellationToken.None, connectionId: fixture.ConnectionId))
            {
                await Assert.That(RequestContext.Keys.Contains(OrleansIdentityConstants.USER_CLAIMS,
                    StringComparer.Ordinal)).IsFalse();
                await Assert.That(RequestContext.Get(GrainRequestStreamProtocol.ContextKey))
                    .IsEqualTo(new GrainRequestContextState(requestId, Guid.Empty, fixture.ConnectionId,
                        fixture.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress));
                var envelope = fixture.Codec.CreateRead(requestId, null, GrainReadKind.Authenticate,
                    OrleansSerialization.Serialize(0));
                var result = await fixture.Grains.GetGrain<IRequestCqrsIdentityProbeGrain>(requestId)
                    .ValidateAsync(envelope, requestId);
                await Assert.That(result.Accepted).IsTrue();
                await Assert.That(result.Subject).IsNull();
            }

            await userSlot.AssertPriorAsync(RequestCqrsPriorContextValue.Object, previousUser);
            await stateSlot.AssertPriorAsync(RequestCqrsPriorContextValue.Object, previousState);
        }
        finally
        {
            userSlot.Restore();
            stateSlot.Restore();
        }
    }

    private static PrincipalRecord PersistPrincipal(RequestCqrsHostedRuntimeFixture fixture, string subject)
    {
        var record = new PrincipalRecord(subject, fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, "*", Capability.All)], ["*"]);
        return fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    private static async Task AssertRestorePairAsync(RequestCqrsHostedRuntimeFixture fixture, PrincipalRecord principal,
        RequestCqrsContextSlot userSlot, RequestCqrsContextSlot stateSlot,
        RequestCqrsPriorContextValue userPrior, RequestCqrsPriorContextValue statePrior)
    {
        var oldUser = userPrior == RequestCqrsPriorContextValue.Object ? new ClaimsPrincipal() : null;
        var oldState = statePrior == RequestCqrsPriorContextValue.Object
            ? new GrainRequestContextState(Guid.NewGuid(), Guid.NewGuid(), fixture.ConnectionId) : null;
        userSlot.SetPrior(userPrior, oldUser);
        stateSlot.SetPrior(statePrior, oldState);
        var graphContext = new object();
        RequestContext.Set(UnrelatedKey, graphContext);
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        using (new GrainRequestIdentityScope(fixture.Services, principal,
            requestId, commandId, CancellationToken.None, connectionId: fixture.ConnectionId))
        {
            var published = (ClaimsPrincipal?)RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS);
            var identity = published?.Identities.Single();
            await Assert.That(published is not null).IsTrue();
            await Assert.That(identity is not null).IsTrue();
            await Assert.That(identity!.AuthenticationType).IsEqualTo(GrainRequestStreamProtocol.AuthenticationType);
            await Assert.That(published!.Claims.Count()).IsEqualTo(1);
            await Assert.That(published.FindFirst(ClaimTypes.NameIdentifier)?.Value).IsEqualTo(principal.Id);
            await Assert.That(identity.HasClaim(ClaimTypes.Role, ProjectedRoleName)).IsFalse();
            await Assert.That(RequestContext.Get(GrainRequestStreamProtocol.ContextKey))
                .IsEqualTo(new GrainRequestContextState(requestId, commandId, fixture.ConnectionId,
                    fixture.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress));
            await Assert.That(ReferenceEquals(RequestContext.Get(UnrelatedKey), graphContext)).IsTrue();
        }

        await userSlot.AssertPriorAsync(userPrior, oldUser);
        await stateSlot.AssertPriorAsync(statePrior, oldState);
        await Assert.That(ReferenceEquals(RequestContext.Get(UnrelatedKey), graphContext)).IsTrue();
    }

}
