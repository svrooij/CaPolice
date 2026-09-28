using Azure.Core;
using Azure.Identity;
using CaPolice.Abstractions;

namespace CaPolice.Core.Authentication;

internal class CredentialContainer : ICredentialContainer
{
    private static readonly CredentialContainer _instance = new();
    internal static CredentialContainer Instance => _instance;

    public bool CredentialsSet => _tokenCredential is not null;

    private CredentialContainer() { }
    private TokenCredential? _tokenCredential;

    public void Clear()
    {
        _tokenCredential = null;
    }

    public async Task<string> GetAccessTokenAsync(string[] scopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nameof(scopes));
        if (_tokenCredential == null)
        {
            throw new InvalidOperationException("TokenCredential is not set. Please configure the credential before requesting an access token.");
        }
        var tokenRequestContext = new TokenRequestContext(scopes);
        var accessToken = await _tokenCredential.GetTokenAsync(tokenRequestContext, cancellationToken).ConfigureAwait(false);
        return accessToken.Token;
    }

    public void UseDefaultCredentials(string clientId, string tenantId)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(clientId, nameof(clientId));
        ArgumentNullException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));
        _tokenCredential = new Azure.Identity.DefaultAzureCredential(
            new Azure.Identity.DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                ExcludeInteractiveBrowserCredential = false,
                ExcludeAzureCliCredential = false,
                InteractiveBrowserCredentialClientId = clientId,
                ExcludeBrokerCredential = false,
                TenantId = tenantId,
            });
    }

    public void UseGitHubActionsWorkloadIdentity(string clientId, string tenantId)
    {
        _tokenCredential = new GithubActionsTokenCredential(clientId, tenantId, httpClient: new System.Net.Http.HttpClient());
    }

    public void UseManagedIdentity(string? clientId = null)
    {
        _tokenCredential = new Azure.Identity.ManagedIdentityCredential(clientId is null ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(clientId));
    }
}
