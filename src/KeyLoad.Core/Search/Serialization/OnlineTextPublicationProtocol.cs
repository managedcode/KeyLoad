namespace KeyLoad.Core.Features.Search;

internal static class OnlineTextPublicationProtocol
{
    internal const string OutcomeAuthorityAlias = "keyload.core.search.online-text-outcome-authority";
    internal const string CurrentPublicationAlias = "keyload.core.search.online-text-current-publication";
    internal const string PhaseCommandAlias = "keyload.core.search.online-text-publication-phase";
    internal const int CurrentFormat = 1;
    internal const string CurrentFamily = "online-text-current";
    internal const string RuntimePurpose = "keyload-online-text-publication-verified";
    internal const string InvalidPublication = "The online text publication does not match its prepared canonical source.";
    internal const string AdministratorRequired = "Online text maintenance requires current administrator authority.";
    internal const string ChangedOriginal = "The online text command ID was already used with different content.";
    internal const string MissingAuthority = "The original online text publication authority is unavailable.";
}
