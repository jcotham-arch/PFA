namespace PFA_FVG_Scanner.Domain.Modules;

public enum ConnectionSetupState { Connected,Configured,NeedsCredentials,ConnectorNotConfigured,Planned }

public sealed record ConnectionSettingsField(string FieldId,string DisplayName,string InputType,bool Secret,
    bool Required,string? CurrentValueHint=null);

public sealed record ConnectionSettingsItem(string ConnectionId,string DisplayName,string Category,
    string Description,string AuthenticationMode,ConnectionSetupState State,string StateDetail,
    IReadOnlyList<string> Capabilities,IReadOnlyList<ConnectionSettingsField> Fields,
    string ConnectivityEvidence,string? OfficialDocumentationUrl,
    bool CredentialMutationAvailable=false,bool RequiresAuthenticatedUser=true,
    bool RequiresEncryptedVault=true);

public sealed record ConnectionSettingsDashboard(IReadOnlyList<ConnectionSettingsItem> Connections,
    bool UserAuthenticationConfigured,bool EncryptedCredentialVaultConfigured,
    string CredentialPolicy="Secrets must never be stored in browser storage, source control, logs, or plain application tables.",
    bool LiveBrokerRoutingEnabled=false);
