using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using System;
using System.IO;
using System.Management.Automation;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Commands;

/// <summary>
/// <para type="synopsis">Exports all conditional access policies from the connected tenant to JSON files.</para>
/// <para type="description">This cmdlet retrieves all conditional access policies from Microsoft Graph and writes each policy to a file in the specified output directory. The file name is controlled by FileNameFormat, which supports {id}, {displayName}, {tag} and {version} as placeholders and may include path separators to create subdirectories. When a display name follows the convention "TAG: Title-vX.Y", {tag} resolves to the prefix before the colon and {version} resolves to the version suffix; both fall back to sensible defaults when absent. Run Connect-CaPolice before using this cmdlet.</para>
/// </summary>
/// <example>
/// <para type="name">Export policies to a folder</para>
/// <para type="description">Export all conditional access policies to the ./Policies directory using the default {id}.json file name.</para>
/// <code>Export-CaPolicePolicy -OutputPath ./Policies</code>
/// </example>
/// <example>
/// <para type="name">Export and overwrite existing files</para>
/// <para type="description">Export all conditional access policies, overwriting any existing JSON files in the output directory.</para>
/// <code>Export-CaPolicePolicy -OutputPath ./Policies -Force</code>
/// </example>
/// <example>
/// <para type="name">Export with display name as file name</para>
/// <para type="description">Export all conditional access policies, using each policy's display name as the file name.</para>
/// <code>Export-CaPolicePolicy -OutputPath ./Policies -FileNameFormat "{displayName}.json"</code>
/// </example>
/// <example>
/// <para type="name">Export into per-policy subdirectories</para>
/// <para type="description">Export each policy into its own subdirectory named after its ID.</para>
/// <code>Export-CaPolicePolicy -OutputPath ./Policies -FileNameFormat "{id}/policy.json"</code>
/// </example>
/// <example>
/// <para type="name">Export with tag subdirectory and version file name</para>
/// <para type="description">For policies following the "TAG: Title-vX.Y" naming convention, group files by tag and include the version. Policies without a tag fall back to their ID; policies without a version fall back to "latest".</para>
/// <code>Export-CaPolicePolicy -OutputPath ./Policies -FileNameFormat "{tag}/{id}-{version}.json"</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsData.Export, "CaPolicePolicy")]
[OutputType(typeof(FileInfo))]
public partial class ExportCaPolicePolicyCommand : DependencyCmdlet<Startup>
{
    private const string GraphPoliciesUrl = "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies";
    private const string DefaultFileNameFormat = "{id}.json";
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

    /// <summary>
    /// Format string for the output file name. Supports {id}, {displayName}, {tag} and {version} as placeholders.
    /// {tag} is extracted from display names following the "TAG: Title" convention; falls back to {id} when absent.
    /// {version} is extracted from display names ending in "-vX.Y"; falls back to "latest" when absent.
    /// Path separators are allowed to create subdirectories under OutputPath, for example {tag}/{id}-{version}.json.
    /// Defaults to {id}.json.
    /// </summary>
    [Parameter(
        Mandatory = false,
        Position = 1)]
    public string FileNameFormat { get; set; } = DefaultFileNameFormat;

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

                    var displayName = policy.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? id : id;
                    var relativePath = ResolveRelativePath(FileNameFormat, id, displayName);
                    var filePath = Path.Combine(outputDir.FullName, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

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

    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();
    // Matches the tag prefix before the first ": ", e.g. "CAU012-RSI" in "CAU012-RSI: Title-v1.0".
    private static readonly Regex _tagPattern = new(@"^([^:]+):\s*", RegexOptions.Compiled);
    // Matches a semantic version suffix at the end, e.g. "v1.0" in "Title-v1.0".
    private static readonly Regex _versionPattern = new(@"-(v\d+(?:\.\d+)*)$", RegexOptions.Compiled);

    // Replaces characters that are invalid in a single file name component so that an expanded
    // placeholder cannot inject unintended path separators or illegal characters.
    private static string SanitizeFileNameComponent(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(_invalidFileNameChars, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars);
    }

    // Extracts optional tag (prefix before ": ") and version (suffix matching -vX.Y) from a display
    // name like "CAU012-RSI: My Policy-v1.0". Falls back to fallbackTag / "latest" when absent.
    private static (string Tag, string Version) ParseDisplayName(string displayName, string fallbackTag)
    {
        var tagMatch = _tagPattern.Match(displayName);
        var versionMatch = _versionPattern.Match(displayName);
        var tag = tagMatch.Success ? tagMatch.Groups[1].Value.Trim() : fallbackTag;
        var version = versionMatch.Success ? versionMatch.Groups[1].Value : "latest";
        return (tag, version);
    }

    // Resolves all placeholders in the format string and normalises any forward slashes to the
    // platform directory separator so subdirectory formats work on all OSes.
    private static string ResolveRelativePath(string format, string id, string displayName)
    {
        var (tag, version) = ParseDisplayName(displayName, id);
        return format
            .Replace("{id}", SanitizeFileNameComponent(id), StringComparison.OrdinalIgnoreCase)
            .Replace("{displayName}", SanitizeFileNameComponent(displayName), StringComparison.OrdinalIgnoreCase)
            .Replace("{tag}", SanitizeFileNameComponent(tag), StringComparison.OrdinalIgnoreCase)
            .Replace("{version}", SanitizeFileNameComponent(version), StringComparison.OrdinalIgnoreCase)
            .Replace('/', Path.DirectorySeparatorChar);
    }
}
