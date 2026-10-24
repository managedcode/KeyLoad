namespace KeyLoad.Core.ResourceExecution;

internal static class ResourceEffectAdmissionProtocol
{
    internal const int Version = 1;
    internal const int VersionId = 0;
    internal const int FingerprintId = 1;
    internal const int BindingsId = 2;
    internal const int ErrorId = 3;
    internal const int DetailId = 4;
    internal const int Zero = 0;
    internal const string Alias = "keyload.resource-retirement.effect-admission";
    internal const string Invalid = "The resource effect admission is invalid.";
    internal const string Unbound = "The operation has no authenticated resource lifetime admission.";
    internal const string Changed = "The admitted resource lifetime has changed.";
    internal const string Capacity = "The resource effect admission exceeds the original operation budget.";
}
