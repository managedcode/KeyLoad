namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryProtocol
{
    internal const int Version = 1;
    internal const int Zero = 0;
    internal const int Next = 1;
    internal const int FirstField = 0;
    internal const int SecondField = 1;
    internal const int ThirdField = 2;
    internal const int FourthField = 3;
    internal const string OriginAlias = "keyload.resource-owner-inventory.origin.v1";
    internal const string EnrollmentAlias = "keyload.resource-owner-inventory.enrollment.v1";
    internal const string WitnessAlias = "keyload.resource-owner-inventory.domain-witness.v1";
    internal const string OriginSpace = "resource-owner-inventory-origin-v1";
    internal const string EntrySpace = "resource-owner-inventory-entry-v1";
    internal const string DomainSpace = "resource-owner-inventory-domain-v1";
    internal const string CreationSpace = "resource-owner-inventory-creation-v1";
    internal const string WitnessSpace = "resource-owner-inventory-domain-witness-v1";
    internal const string Invalid = "The native resource owner inventory metadata is invalid.";
    internal const string Unknown = "The native resource owner inventory origin is not proven.";
    internal const string Capacity = "The native resource owner inventory exceeds the operation budget.";
}
