using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using System;
using System.IO;
using System.Management.Automation;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Commands;

/// <summary>
/// <para type="synopsis">Exports all conditional access policies from the connected tenant to JSON files.</para>
/// <para type="description">This cmdlet retrieves all conditional access policies from Microsoft Graph and writes each policy to a JSON file named {id}.json in the specified output directory. Run Connect-CaPolice before using this cmdlet.</para>
/// </summary>
/// <example>
/// <para type="name">Export policies to a folder</para>
/// <para type="description">Export all conditional access policies to the ./Policies directory.</para>
/// <code>Export-CaPolicePolicy -OutputPath ./Policies</code>
/// </example>
/// <example>
/// <para type="name">Export and overwrite existing files</para>
/// <para type="description">Export all conditional access policies, overwriting any existing JSON files in the output directory.</para>
/// <code>Export-CaPolicePolicy -OutputPath ./Policies -Force</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsData.Export, "CaPolicePolicy")]
[OutputType(typeof(FileInfo))]
public partial class ExportCaPolicePolicyCommand : DependencyCmdlet<Startup>
{
    private const string GraphPoliciesUrl = "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies";
    private static readonly string[] RequiredScopes = ["Policy.Read.All"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// The path to the directory where the JSON files will be written. The directory is created if it does not exist.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipelineByPropertyName = true)]
    public string OutputPath { get; set; }

    /// <summary>
    /// Overwrite existing JSON files in the output directory. Without this switch, existing files are skipped.
    /// </summary>
    [Parameter(
        Mandatory = false)]
    public SwitchParameter Force { get; set; }

    [ServiceDependency(Required = true)]
    private ILogger<ExportCaPolicePolicyCommand> _logger;

    [ServiceDependency(Required = true)]
    private Authentication.CredentialContainer _credentialContainer;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        if (_credentialContainer.TokenCredential is null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException("Not connected to Graph. Run Connect-CaPolice first."),
                "NotConnected",
                ErrorCategory.AuthenticationError,
                null));
            return;
        }

        var outputDir = new DirectoryInfo(OutputPath);
        if (!outputDir.Exists)
        {
            _logger.LogInformation("Creating output directory {OutputPath}", outputDir.FullName);
            outputDir.Create();
        }

        var tokenResult = await _credentialContainer.TokenCredential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(RequiredScopes), cancellationToken);

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        var nextUrl = GraphPoliciesUrl;
        var count = 0;

        while (nextUrl is not null)
        {
            _logger.LogDebug("Fetching policies from {Url}", nextUrl);
            using var response = await httpClient.GetAsync(nextUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var policies))
            {
                foreach (var policy in policies.EnumerateArray())
                {
                    if (!policy.TryGetProperty("id", out var idProperty))
                        continue;
                    var id = idProperty.GetString();
                    if (id is null)
                        continue;

                    var filePath = Path.Combine(outputDir.FullName, $"{id}.json");

                    if (File.Exists(filePath) && !Force)
                    {
                        _logger.LogWarning("File {FilePath} already exists. Use -Force to overwrite.", filePath);
                        continue;
                    }

                    var json = JsonSerializer.Serialize(policy, JsonOptions);
                    await File.WriteAllTextAsync(filePath, json, cancellationToken);
                    count++;
                    WriteObject(new FileInfo(filePath));
                }
            }

            nextUrl = root.TryGetProperty("@odata.nextLink", out var nextLink) ? nextLink.GetString() : null;
        }

        _logger.LogInformation("Exported {Count} conditional access {Noun} to {OutputPath}",
            count,
            count == 1 ? "policy" : "policies",
            outputDir.FullName);
    }
}
