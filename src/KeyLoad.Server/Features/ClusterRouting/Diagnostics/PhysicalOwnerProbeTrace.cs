namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeTrace
{
    private const string ObservedPrefix = "; Observed=";
    private const string RefusedPrefix = "; Refused=";
    internal PhysicalOwnerProbeStage Stage { get; set; }
    private PhysicalOwnerProbePredicate observed;
    private PhysicalOwnerProbePredicate refused;

    internal bool Refused(PhysicalOwnerProbePredicate predicate, bool failed)
    {
        observed |= predicate;
        if (failed)
        { refused |= predicate; }
        return failed;
    }

    public override string ToString() => $"{Stage}{ObservedPrefix}{observed}{RefusedPrefix}{refused}";
}
