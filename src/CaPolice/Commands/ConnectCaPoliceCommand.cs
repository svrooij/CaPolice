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
/// <parameterSet>
/// <para type="name">DefaultCredentials</para>
/// <para type="description">Connect to Graph using DefaultAzureCredential with default settings.</para>
/// </parameterSet>
/// <parameterSet>
/// <para type="name">ManagedIdentity</para>
/// <para type="description">Connect to Graph using managed identity with default settings.</para>
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

    //private const string InteractiveBrowserClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";
    private const string InteractiveBrowserClientId = "463147f6-19da-494d-9897-b7285740d804";
    private static readonly string[] DefaultScopes = ["https://graph.microsoft.com/.default"];
    private static readonly string[] InteractiveScopes = ["Policy.Read.All"];
    /// <summary>
    /// Specify the client ID for the authentication, is load from the environment variable AZURE_CLIENT_ID if not specified.
    /// </summary>
    [Parameter(
    Mandatory = false,
    Position = 2,
    ValueFromPipelineByPropertyName = true, ParameterSetName = GitHubParameterSet)]
    [Parameter(
    Mandatory = false,
    Position = 2,
    ValueFromPipelineByPropertyName = true, ParameterSetName = DefaultCredentialsParameterSet)]
    public string? ClientId { get; set; } = Environment.GetEnvironmentVariable(Authentication.GithubActionsTokenCredential.AZURE_CLIENT_ID);

    /// <summary>
    /// Specify the Tenant ID for the authentication, is load from the environment variable AZURE_TENANT_ID if not specified.
    /// </summary>
    [Parameter(
    Mandatory = false,
    Position = 1,
    ValueFromPipelineByPropertyName = true, ParameterSetName = GitHubParameterSet)]
    [Parameter(
    Mandatory = false,
    Position = 1,
    ValueFromPipelineByPropertyName = true, ParameterSetName = DefaultCredentialsParameterSet)]
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

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to CaPolice, using {ParameterSet}...", ParameterSetName);

        switch (ParameterSetName)
        {
            case DefaultCredentialsParameterSet:
                _credentialContainer.TokenCredential = new Azure.Identity.DefaultAzureCredential(
                    new Azure.Identity.DefaultAzureCredentialOptions
                    {
                        // On a developer machine (not running in Azure), the managed identity and
                        // workload identity probes hit the IMDS endpoint (169.254.169.254), which is
                        // unreachable and surfaces a fatal error that stops the credential chain
                        // before it can fall back to the interactive browser. Both flows have their
                        // own parameter sets (-UseManagedIdentity / -Github), so exclude them here so
                        // DefaultAzureCredential can fall back to the interactive browser as intended.
                        ExcludeManagedIdentityCredential = true,
                        ExcludeWorkloadIdentityCredential = true,
                        ExcludeInteractiveBrowserCredential = false,
                        ExcludeAzureCliCredential = false,
                        InteractiveBrowserCredentialClientId = ClientId ?? InteractiveBrowserClientId,
                        ExcludeBrokerCredential = false,
                        TenantId = TenantId,
                    });
                break;
            case GitHubParameterSet:
                _credentialContainer.TokenCredential = new Authentication.GithubActionsTokenCredential(ClientId, TenantId, httpClient: new System.Net.Http.HttpClient());
                break;
            case ManagedIdentityParameterSet:
                _credentialContainer.TokenCredential = new Azure.Identity.ManagedIdentityCredential(new Azure.Identity.ManagedIdentityCredentialOptions());
                break;
            default:
                return;
        }

        if (Test)
        {
            var scopes = ParameterSetName == DefaultCredentialsParameterSet ? InteractiveScopes : DefaultScopes;
            var token = await _credentialContainer.TokenCredential!.GetTokenAsync(new Azure.Core.TokenRequestContext(scopes), cancellationToken);
            _logger.LogDebug("Token: {Token}", token.Token);
            WriteObject(token.Token);
        }
    }
}
