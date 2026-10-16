using System.Globalization;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsControlledLifetimeNames
{
    internal static string PlanName(Guid armId) => PlanPrefix + Id(armId) + JsonSuffix;
    internal static string StepName(Guid armId, Guid requestId, int step)
        => StepPrefix + Scope(armId, requestId, step) + JsonSuffix;
    internal static string AckName(Guid armId, Guid requestId, int step)
        => AckPrefix + Scope(armId, requestId, step) + JsonSuffix;
    internal static string PartName(Guid armId, Guid requestId, int step, string role, int index)
        => PartPrefix + Scope(armId, requestId, step) + Separator + role + Separator + Number(index) + PartSuffix;

    internal static bool IsName(string name)
    {
        if (name.StartsWith(PartPrefix, StringComparison.Ordinal))
        {
            try
            {
                _ = ParsePart(name);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        if (name.StartsWith(PlanPrefix, StringComparison.Ordinal))
        {
            return name.Length > PlanPrefix.Length + JsonSuffix.Length && name.EndsWith(JsonSuffix, StringComparison.Ordinal)
                && Guid.TryParseExact(name.AsSpan(PlanPrefix.Length, name.Length - PlanPrefix.Length - JsonSuffix.Length),
                    RequestCqrsProbeProtocol.SessionIdFormat, out var arm) && arm != Guid.Empty && name == PlanName(arm);
        }
        return Scoped(name, StepPrefix) || Scoped(name, AckPrefix);
    }

    private static bool Scoped(string name, string prefix)
    {
        const int ScopeFields = 3;
        const int ArmIndex = 0;
        const int RequestIndex = 1;
        const int StepIndex = 2;
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(JsonSuffix, StringComparison.Ordinal) || name.Length <= prefix.Length + JsonSuffix.Length)
        {
            return false;
        }
        var values = name[prefix.Length..^JsonSuffix.Length].Split(Separator, StringSplitOptions.None);
        return values.Length == ScopeFields
            && Guid.TryParseExact(values[ArmIndex], RequestCqrsProbeProtocol.SessionIdFormat, out var arm)
            && Guid.TryParseExact(values[RequestIndex], RequestCqrsProbeProtocol.SessionIdFormat, out var request)
            && arm != Guid.Empty && request != Guid.Empty
            && int.TryParse(values[StepIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var step)
            && step is >= BaselineStep and <= LastNegativeStep && name == prefix + Scope(arm, request, step) + JsonSuffix;
    }

    internal static (Guid Arm, Guid Request, int Step, string Role, int Index) ParsePart(string name)
    {
        if (!name.StartsWith(PartPrefix, StringComparison.Ordinal) || !name.EndsWith(PartSuffix, StringComparison.Ordinal) || name.Length <= PartPrefix.Length + PartSuffix.Length)
        {
            throw new InvalidOperationException(Invalid);
        }
        var values = name[PartPrefix.Length..^PartSuffix.Length].Split(Separator, StringSplitOptions.None);
        if (values.Length != PartFields || !Guid.TryParseExact(values[PartArmIndex], RequestCqrsProbeProtocol.SessionIdFormat, out var arm)
            || !Guid.TryParseExact(values[PartRequestIndex], RequestCqrsProbeProtocol.SessionIdFormat, out var request)
            || !int.TryParse(values[PartStepIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var step)
            || !int.TryParse(values[PartIndexIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || arm == Guid.Empty || request == Guid.Empty || step is < BaselineStep or > LastNegativeStep || index < FirstIndex
            || values[PartRoleIndex] is not (CarrierRole or RequestRole or ReplyRole)
            || name != PartName(arm, request, step, values[PartRoleIndex], index))
        {
            throw new InvalidOperationException(Invalid);
        }
        return (arm, request, step, values[PartRoleIndex], index);
    }

    private static string Scope(Guid arm, Guid request, int step) => Id(arm) + Separator + Id(request) + Separator + Number(step);
    private static string Id(Guid value) => value.ToString(RequestCqrsProbeProtocol.SessionIdFormat);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
