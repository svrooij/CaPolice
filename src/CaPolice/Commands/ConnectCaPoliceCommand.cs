using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using System;
using System.Management.Automation;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Commands;


/// <summary>
/// <para type="synopsis">Connects to CaPolice to Graph.</para>
/// <para type="description">This cmdlet connects to CaPolice to Graph using the specified authentication method.</para>
/// </summary>
/// <parameterSet>
/// <para type="name">GitHub</para>
/// <para type="description">Connect to Graph using GitHub Actions workload identity.</para>
/// </parameterSet>
/// <example>
/// <para type="name">GitHub Actions workload identity</para>
/// <para type="description">Connect to Graph using GitHub Actions workload identity.</para>
/// <code>Connect-CaPolice -Github</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsCommunications.Connect, "CaPolice", DefaultParameterSetName = DefaultCredentialsParameterSet)]
[OutputType(typeof(string))]
public partial class ConnectCaPoliceCommand : DependencyCmdlet<Startup>
{
    private const string DefaultCredentialsParameterSet = "DefaultCredentials";
    private const string GitHubParameterSet = "GitHub";
    private const string ManagedIdentityParameterSet = "ManagedIdentity";
    /// <summary>
    /// Specify the client ID for the authentication, is load from the environment variable AZURE_CLIENT_ID if not specified.
    /// </summary>
    [Parameter(
    Mandatory = false,
    Position = 2,
    ValueFromPipelineByPropertyName = true, ParameterSetName = GitHubParameterSet)]
    public string? ClientId { get; set; } = Environment.GetEnvironmentVariable(Authentication.GithubActionsTokenCredential.AZURE_CLIENT_ID);

    /// <summary>
    /// Specify the Tenant ID for the authentication, is load from the environment variable AZURE_TENANT_ID if not specified.
    /// </summary>
    [Parameter(
    Mandatory = false,
    Position = 1,
    ValueFromPipelineByPropertyName = true, ParameterSetName = GitHubParameterSet)]
    public string? TenantId { get; set; } = Environment.GetEnvironmentVariable(Authentication.GithubActionsTokenCredential.AZURE_TENANT_ID);

    /// <summary>
    /// Try connect to Graph using GitHub Actions workload identity.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = GitHubParameterSet)]
    public SwitchParameter Github { get; set; }

    /// <summary>
    /// Try connect to Graph using Managed Identity.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = ManagedIdentityParameterSet)]
    public SwitchParameter UseManagedIdentity { get; set; }

    /// <summary>
    /// Try connect to Graph using DefaultAzureCredential.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = DefaultCredentialsParameterSet)]
    public SwitchParameter UseDefaultCredentials { get; set; }

    /// <summary>
    /// Test the connection by retrieving a token from Graph and output it to the console.
    /// </summary>
    [Parameter(
        Mandatory = false,
        Position = 20,
        ParameterSetName = DefaultCredentialsParameterSet)]
    [Parameter(
        Mandatory = false,
        Position = 20,
        ParameterSetName = GitHubParameterSet)]
    [Parameter(
        Mandatory = false,
        Position = 20,
        ParameterSetName = ManagedIdentityParameterSet)]
    public SwitchParameter Test { get; set; }

    [ServiceDependency(Required = true)]
    private ILogger<ConnectCaPoliceCommand> _logger;

    [ServiceDependency(Required = true)]
    private Authentication.CredentialContainer _credentialContainer;

    [ServiceDependency(Required = true)]
    private System.Net.Http.IHttpClientFactory _httpClientFactory;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to CaPolice, using {ParameterSet}...", ParameterSetName);

        switch (ParameterSetName)
        {
            case DefaultCredentialsParameterSet:
                _credentialContainer.TokenCredential = new Azure.Identity.DefaultAzureCredential(new Azure.Identity.DefaultAzureCredentialOptions());
                break;
            case GitHubParameterSet:
                _credentialContainer.TokenCredential = new Authentication.GithubActionsTokenCredential(ClientId, TenantId, httpClient: _httpClientFactory.CreateClient());
                break;
            case ManagedIdentityParameterSet:
                _credentialContainer.TokenCredential = new Azure.Identity.ManagedIdentityCredential(new Azure.Identity.ManagedIdentityCredentialOptions());
                break;
            default:
                return;
        }

        if(Test)
        {
            var token = await _credentialContainer.TokenCredential!.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }), cancellationToken);
            _logger.LogDebug("Token: {Token}", token.Token);
            WriteObject(token);
        }
    }
}
