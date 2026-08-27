/*
 * This code is copied from https://github.com/svrooij/Azure.Identity.Federation/blob/220e439bb5b6ef12cdccf7952bcb67fa9eccae0d/Azure.Identity.Federation/GithubActionsTokenCredential.cs
 * Author: svrooij
 * License: MIT https://github.com/svrooij/Azure.Identity.Federation/blob/220e439bb5b6ef12cdccf7952bcb67fa9eccae0d/LICENSE.txt
 */
using Azure.Core;
using Azure.Identity;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Authentication;

/// <summary>
/// Represents a token credential that retrieves an ID token from GitHub Actions OIDC and uses it to authenticate with Entra ID.
/// </summary>
public class GithubActionsTokenCredential : TokenCredential
{
    private const string ActionsRequestTokenKey = "ACTIONS_ID_TOKEN_REQUEST_TOKEN";
    private const string ActionsRequestUrlKey = "ACTIONS_ID_TOKEN_REQUEST_URL";
    private const string DefaultIdTokenAudience = "api://AzureADTokenExchange";

    internal const string AZURE_TENANT_ID = "AZURE_TENANT_ID";
    internal const string AZURE_CLIENT_ID = "AZURE_CLIENT_ID";

    private readonly string? _requestToken;
    private readonly string? _requestUrl;
    private readonly string IdTokenAudience;
    private readonly string TenantId;
    private readonly string ClientId;
    private readonly HttpClient httpClient;
    private ClientAssertionCredential? clientAssertionCredential;

    /// <summary>
    /// Initializes a new instance of the <see cref="GithubActionsTokenCredential"/> class.
    /// </summary>
    /// <param name="tenantId">Override the tenant ID, as loaded from the `AZURE_TENANT_ID` variable</param>
    /// <param name="clientId">Override the client ID, as loaded from the `AZURE_CLIENT_ID` variable</param>
    /// <param name="idTokenAudience">Override the ID token audience, default `api://AzureADTokenExchange`</param>
    /// <param name="httpClient">Optional HTTP client to use for requests</param>
    public GithubActionsTokenCredential(string? tenantId = null, string? clientId = null, string? idTokenAudience = DefaultIdTokenAudience, HttpClient? httpClient = null)
    {
        IdTokenAudience = idTokenAudience ?? DefaultIdTokenAudience;
        _requestToken = Environment.GetEnvironmentVariable(ActionsRequestTokenKey);
        _requestUrl = Environment.GetEnvironmentVariable(ActionsRequestUrlKey);
        this.httpClient = httpClient ?? new HttpClient();
        this.httpClient.DefaultRequestHeaders.UserAgent.Clear();
        this.httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Azure.Identity.Federation", "1.0"));
        // Not sure if this is needed, see https://github.com/actions/toolkit/blob/c5c786523e095ca3fabfc4d345e16242da34e108/packages/core/src/oidc-utils.ts#L22
        this.httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("actions/oidc-client", "1.0"));
        TenantId = tenantId ?? Environment.GetEnvironmentVariable(AZURE_TENANT_ID) ?? "";
        ClientId = clientId ?? Environment.GetEnvironmentVariable(AZURE_CLIENT_ID) ?? "";
    }

    /// <summary>
    /// Gets an access token from GitHub Actions OIDC and uses it to authenticate with Entra ID.
    /// </summary>
    /// <param name="requestContext"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        ValidateSettings();
        clientAssertionCredential ??= new ClientAssertionCredential(TenantId, ClientId, (cancellationToken) => GetIdToken(cancellationToken));

        return clientAssertionCredential.GetToken(requestContext, cancellationToken);
    }

    /// <summary>
    /// Gets an access token from GitHub Actions OIDC and uses it to authenticate with Entra ID asynchronously.
    /// </summary>
    /// <param name="requestContext"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        ValidateSettings();
        clientAssertionCredential ??= new ClientAssertionCredential(TenantId, ClientId, (cancellationToken) => GetIdToken(cancellationToken));
        return clientAssertionCredential.GetTokenAsync(requestContext, cancellationToken);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_requestToken) || string.IsNullOrWhiteSpace(_requestUrl) || !Uri.TryCreate(_requestUrl, UriKind.Absolute, out _))
        {
            throw new CredentialUnavailableException($"Environment variables '{ActionsRequestTokenKey}' and/or '{ActionsRequestUrlKey}' are not set.");
        }

        if (string.IsNullOrWhiteSpace(IdTokenAudience))
        {
            throw new ArgumentException("Audience must be set.", nameof(IdTokenAudience));
        }

        if (string.IsNullOrWhiteSpace(TenantId))
        {
            throw new ArgumentException("Tenant ID must be set", nameof(TenantId));
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new ArgumentException("Client ID must be set", nameof(ClientId));
        }
    }

    private async Task<string> GetIdToken(CancellationToken cancellationToken)
    {
        var uri = new Uri($"{_requestUrl!}&audience={System.Web.HttpUtility.UrlEncode(IdTokenAudience!)}");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _requestToken!);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new CredentialUnavailableException($"Request to '{uri}' failed with status code '{response.StatusCode}'.");
        var result = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);

        return result!.value;
    }
}

/// <summary>
/// Represents the response from the GitHub Actions OIDC token endpoint.
/// </summary>
public class GitHubTokenResponse
{
    /// <summary>
    /// Gets or sets the ID token value returned by the GitHub Actions OIDC token endpoint.
    /// </summary>
    public string value { get; set; }
}