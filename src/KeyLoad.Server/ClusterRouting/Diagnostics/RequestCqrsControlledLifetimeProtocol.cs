namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsControlledLifetimeProtocol
{
    internal const string PlanKind = "ControlledLifetimePlan";
    internal const string StepKind = "ControlledLifetimeStep";
    internal const string AckKind = "ControlledLifetimeAck";
    internal const string PlanPrefix = "controlled-lifetime-plan-";
    internal const string StepPrefix = "controlled-lifetime-step-";
    internal const string AckPrefix = "controlled-lifetime-ack-";
    internal const string PartPrefix = "controlled-lifetime-part-";
    internal const string CarrierRole = "carrier";
    internal const string RequestRole = "request";
    internal const string ReplyRole = "reply";
    internal const string JsonSuffix = ".json";
    internal const string PartSuffix = ".bin";
    internal const string Separator = "-";
    internal const string EmptyDigest = "";
    internal const string Invalid = "The controlled lifetime private probe evidence is invalid.";
    internal const int BaselineStep = 0;
    internal const int FirstNegativeStep = 1;
    internal const int SubstitutedStep = 2;
    internal const int LastNegativeStep = 3;
    internal const int IndexStep = 1;
    internal const int EmptyBytes = 0;
    internal const int FirstIndex = 0;
    internal const int Single = 1;
    internal const int DigestCharacters = 64;
    internal const int PartFields = 5;
    internal const int PartArmIndex = 0;
    internal const int PartRequestIndex = 1;
    internal const int PartStepIndex = 2;
    internal const int PartRoleIndex = 3;
    internal const int PartIndexIndex = 4;
}
