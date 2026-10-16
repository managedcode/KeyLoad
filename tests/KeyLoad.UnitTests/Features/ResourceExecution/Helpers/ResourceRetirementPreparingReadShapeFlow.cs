using KeyLoad.Clustering;
using KeyLoad.Server.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparingReadShapeFlow
{
    internal static Task RequestsAsync() => ResourceRetirementPreparationValidationFixture.RunAsync(async (state, token) =>
    {
        using var native = new ReplicaSecurityFixture();
        var seed = ResourceRetirementPreparingShapeData.Seed(state);
        var outer = ResourceRetirementPreparingShapeData.Outer(native, seed);
        var queries = new[] { ResourceRetirementReadOperation.Preparation, ResourceRetirementReadOperation.OwnerStatus,
            ResourceRetirementReadOperation.Attempt, ResourceRetirementReadOperation.PreparedOperation,
            ResourceRetirementReadOperation.IssueOwnerReservation }.Select(operation =>
                ResourceRetirementPreparingShapeData.Read(outer, seed, operation)).ToArray();
        foreach (var query in queries)
        {
            token.ThrowIfCancellationRequested();
            await Assert.That(query.OriginalPolicyEpoch).IsEqualTo(0L);
            ResourceRetirementNativeReadValidation.RequireRequest(outer with
            {
                ExpiresAt = query.ExpiresAt
            },
                ResourceRetirementPreparingShapeData.RoundTrip(query), seed.Source.Database.Limits);
        }
        var status = queries.Single(query => query.Operation == ResourceRetirementReadOperation.OwnerStatus);
        foreach (var query in new[]
        {
            queries.First() with { Preparation = seed.Control }, status with { AttemptId = Guid.NewGuid() },
            status with { Context = seed.Context }, status with { Request = seed.Request with { RetirementId = Guid.NewGuid() } },
            status with { Operation = ResourceRetirementReadOperation.State },
            status with { Operation = (ResourceRetirementReadOperation)8 },
            queries.Last() with { Attempt = null }, queries.Last() with { Context = null }
        })
        {
            await ResourceRetirementPreparingShapeData.RefusalAsync(() => ResourceRetirementNativeReadValidation.RequireRequest(
                outer with
                {
                    ExpiresAt = query.ExpiresAt
                }, ResourceRetirementPreparingShapeData.RoundTrip(query), seed.Source.Database.Limits),
                ErrorCode.Unauthenticated);
        }
        ResourceRetirementNativeReadValidation.RequireRequest(outer, status, seed.Source.Database.Limits);
    });

    internal static Task RepliesAsync() => ResourceRetirementPreparationValidationFixture.RunAsync(async (state, token) =>
    {
        using var native = new ReplicaSecurityFixture();
        var seed = ResourceRetirementPreparingShapeData.Seed(state);
        var outer = ResourceRetirementPreparingShapeData.Outer(native, seed);
        var preparation = ResourceRetirementPreparingShapeData.Read(outer, seed, ResourceRetirementReadOperation.Preparation);
        var status = ResourceRetirementPreparingShapeData.Read(outer, seed, ResourceRetirementReadOperation.OwnerStatus);
        var attempt = ResourceRetirementPreparingShapeData.Read(outer, seed, ResourceRetirementReadOperation.Attempt);
        var prepared = ResourceRetirementPreparingShapeData.Read(outer, seed, ResourceRetirementReadOperation.PreparedOperation);
        var issue = ResourceRetirementPreparingShapeData.Read(outer, seed, ResourceRetirementReadOperation.IssueOwnerReservation);
        var absent = new ResourceRetirementReadReplyV1(null, null, null, 0, 0);
        var work = ResourceRetirementPreparationValidationFixture.Work(seed.Source, token);
        var actualAttempt = seed.Source.Database.ReadResourceRetirementAttempt(PhysicalShardCatalogFixture.RootPrincipalId,
            seed.Context, seed.Attempt.AttemptId, work);
        var actualPrepared = seed.Source.Database.ReadPreparedResourceRetirementOperation(PhysicalShardCatalogFixture.RootPrincipalId,
            seed.Context, seed.Attempt, ResourceRetirementPreparationValidationFixture.Work(seed.Source, token));
        var pairs = new[] { (preparation, absent with { Preparation = state.Preparation }),
            (status, absent with { OwnerStatus = state.Statuses.First() }), (attempt, absent with { Attempt = actualAttempt }),
            (prepared, absent with { PreparedOperation = actualPrepared }), (issue, absent with { OriginalOperation = actualPrepared.Original }) };
        foreach (var pair in pairs.Concat(new[] { (preparation, absent), (status, absent), (prepared, absent) }))
        {
            ResourceRetirementNativeReadValidation.RequireReply(pair.Item1,
                ResourceRetirementPreparingShapeData.RoundTrip(pair.Item2), seed.Source.Database.Limits);
        }
        foreach (var pair in new[] { (attempt, absent), (issue, absent), (preparation, pairs.Last().Item2),
            (status, pairs.First().Item2), (issue, pairs[2].Item2), (prepared, pairs.Last().Item2) })
        {
            await ResourceRetirementPreparingShapeData.RefusalAsync(() => ResourceRetirementNativeReadValidation.RequireReply(
                pair.Item1, ResourceRetirementPreparingShapeData.RoundTrip(pair.Item2), seed.Source.Database.Limits), ErrorCode.Unauthenticated);
        }
        ResourceRetirementNativeReadValidation.RequireReply(issue, pairs.Last().Item2, seed.Source.Database.Limits);
    });
}
