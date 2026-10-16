namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Closes the three durable-attempt purposes over their exact original source authority.</summary>
internal static class ResourceRetirementAttemptContextValidation
{
    internal static void Require(ResourceRetirementAttemptContextV1 context, DatabaseLimits limits)
    {
        Shape(context);
        if (context.Purpose is ResourceRetirementAttemptPurpose.SealBarrier or ResourceRetirementAttemptPurpose.CleanupPage)
        {
            ResourceRetirementValidation.Control(context.Retirement!, limits);
            if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
            {
                ResourceRetirementCleanupAttemptScope.Require(context, limits);
            }
        }
        else
        {
            ResourceRetirementPreparationValidation.Control(context.Preparation!, limits);
            ResourceRetirementPreparationValidation.Member(context.Preparation!, context.Receiver!);
        }
        ResourceRetirementValidation.Bound(context, limits);
    }

    private static void Shape(ResourceRetirementAttemptContextV1 context)
    {
        if (context is null || !Enum.IsDefined(context.Purpose)
            || context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier
                && (context.Retirement is null || context.Preparation is not null || context.Receiver is not null || context.Page is not null)
            || context.Purpose == ResourceRetirementAttemptPurpose.OwnerReservation
                && (context.Retirement is not null || context.Preparation is null || context.Receiver is null || context.Page is not null)
            || context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage
                && (context.Retirement is null || context.Preparation is not null || context.Receiver is null || context.Page is null))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    internal static ResourceRetirementRequestV1 Request(ResourceRetirementAttemptContextV1 context)
    {
        Shape(context);
        return context.Purpose is ResourceRetirementAttemptPurpose.SealBarrier or ResourceRetirementAttemptPurpose.CleanupPage
            ? context.Retirement!.Request : context.Preparation!.Request;
    }

    internal static ResourceLifetimeV1 Lifetime(ResourceRetirementAttemptContextV1 context)
    {
        Shape(context);
        return context.Purpose is ResourceRetirementAttemptPurpose.SealBarrier or ResourceRetirementAttemptPurpose.CleanupPage
            ? context.Retirement!.Lifetime : context.Preparation!.Lifetime;
    }

    internal static PhysicalShardRecord SourceOwner(ResourceRetirementAttemptContextV1 context)
    {
        Shape(context);
        return context.Purpose is ResourceRetirementAttemptPurpose.SealBarrier or ResourceRetirementAttemptPurpose.CleanupPage
            ? context.Retirement!.Catalog.DefaultShard : context.Preparation!.Catalog.DefaultShard;
    }

    internal static PhysicalShardRecord ExecutionOwner(ResourceRetirementAttemptContextV1 context)
    {
        Shape(context);
        return context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier
            ? context.Retirement!.Catalog.DefaultShard : context.Receiver!.Owner;
    }

    internal static string IntentDigest(ResourceRetirementAttemptContextV1 context)
    {
        Shape(context);
        return context.Purpose is ResourceRetirementAttemptPurpose.SealBarrier or ResourceRetirementAttemptPurpose.CleanupPage
            ? context.Retirement!.IntentDigest : context.Preparation!.IntentDigest;
    }
}
