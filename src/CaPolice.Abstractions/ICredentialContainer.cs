namespace CaPolice.Abstractions;

public interface ICredentialContainer
{
    void Clear();
    bool CredentialsSet { get; }
    Task<string> GetAccessTokenAsync(string[] scopes, CancellationToken cancellationToken = default);
    void UseDefaultCredentials(string clientId, string tenantId);
    void UseGitHubActionsWorkloadIdentity(string clientId, string tenantId);
    void UseManagedIdentity(string? clientId = null);
}
