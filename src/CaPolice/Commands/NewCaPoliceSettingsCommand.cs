using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Commands;

/// <summary>
/// <para type="synopsis">Creates a new CaPolice settings file.</para>
/// <para type="description">Creates a new settings JSON file for CaPolice. If -PolicyFolder is specified, all *.json policy files in that folder are read and added as policy entries; the tag, version, name and description are parsed from each policy's displayName field following the "TAG: Title-vX.Y" convention. When -NewTenant is specified, policy IDs are omitted, every imported policy's status is forced to "report", and any user or group IDs found in the policy JSON are written into the settings entry as ORG_&lt;id&gt; placeholders. These placeholders intentionally violate the settings schema so that the settings file cannot be published until each ORG_ value has been replaced with a valid target-tenant object ID.</para>
/// </summary>
/// <example>
/// <para type="name">Create a minimal settings file</para>
/// <para type="description">Create a new settings file for the given tenant with a single breakglass user.</para>
/// <code>New-CaPoliceSettings -SettingsFile ./settings.json -TenantId "00000000-0000-0000-0000-000000000000" -BreakglassUsers "user-object-id"</code>
/// </example>
/// <example>
/// <para type="name">Import policies from an exported folder</para>
/// <para type="description">Create a settings file by importing all policy JSON files from a folder previously populated by Export-CaPolicePolicy.</para>
/// <code>New-CaPoliceSettings -SettingsFile ./settings.json -TenantId "00000000-0000-0000-0000-000000000000" -BreakglassUsers "user-object-id" -PolicyFolder ./Policies</code>
/// </example>
/// <example>
/// <para type="name">Create settings for a new tenant</para>
/// <para type="description">Import policies from a folder, omitting IDs and setting all statuses to "report" for deployment to a new tenant.</para>
/// <code>New-CaPoliceSettings -SettingsFile ./settings.json -TenantId "00000000-0000-0000-0000-000000000000" -BreakglassGroups "group-object-id" -PolicyFolder ./Policies -NewTenant</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsCommon.New, "CaPoliceSettings", DefaultParameterSetName = DefaultParameterSet)]
[OutputType(typeof(FileInfo))]
public partial class NewCaPoliceSettingsCommand : DependencyCmdlet<Startup>
{
    private const string SchemaUrl = "https://raw.githubusercontent.com/svrooij/CaPolice/v0.0.5/settings/settings.schema.json";
    private const string DefaultParameterSet = "Default";
    private const string NewTenantParameterSet = "NewTenant";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Path to the settings file to create. Throws an error if the file already exists.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipelineByPropertyName = true)]
    public string SettingsFile { get; set; }

    /// <summary>
    /// The Entra ID tenant ID that the policies are managed in.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 1,
        ValueFromPipelineByPropertyName = true)]
    public string TenantId { get; set; }

    /// <summary>
    /// One or more break-glass user object IDs that are excluded from all conditional access policies.
    /// </summary>
    [Parameter(
        Mandatory = false)]
    public string[]? BreakglassUsers { get; set; }

    /// <summary>
    /// One or more break-glass group object IDs that are excluded from all conditional access policies.
    /// </summary>
    [Parameter(
        Mandatory = false)]
    public string[]? BreakglassGroups { get; set; }

    /// <summary>
    /// Path to a folder containing JSON policy files exported by Export-CaPolicePolicy. All *.json files in the folder are added as policy entries. The tag extracted from each policy's displayName is used as the settings key.
    /// </summary>
    [Parameter(
        Mandatory = false)]
    public string? PolicyFolder { get; set; }

    /// <summary>
    /// Activates new-tenant mode: policy IDs are omitted, all statuses are forced to "report", and user/group IDs from the source tenant are written as ORG_&lt;id&gt; placeholders. Combine with -SourceBreakglassUsers and -SourceBreakglassGroups to drop source-tenant breakglass accounts instead of turning them into placeholders.
    /// </summary>
    [Parameter(
        Mandatory = true,
        ParameterSetName = NewTenantParameterSet)]
    public SwitchParameter NewTenant { get; set; }

    /// <summary>
    /// Source-tenant break-glass user object IDs to exclude from ORG_ placeholder generation. Matching IDs in the policy's excludeUsers list are dropped cleanly because they will be replaced by the new tenant's break-glass users. Only valid with -NewTenant.
    /// </summary>
    [Parameter(
        Mandatory = false,
        ParameterSetName = NewTenantParameterSet)]
    public string[]? SourceBreakglassUsers { get; set; }

    /// <summary>
    /// Source-tenant break-glass group object IDs to exclude from ORG_ placeholder generation. Matching IDs in the policy's excludeGroups list are dropped cleanly because they will be replaced by the new tenant's break-glass groups. Only valid with -NewTenant.
    /// </summary>
    [Parameter(
        Mandatory = false,
        ParameterSetName = NewTenantParameterSet)]
    public string[]? SourceBreakglassGroups { get; set; }

    [ServiceDependency(Required = true)]
    private ILogger<NewCaPoliceSettingsCommand> _logger;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        var settingsFileInfo = new FileInfo(SettingsFile);

        if (settingsFileInfo.Exists)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException($"Settings file '{settingsFileInfo.FullName}' already exists. Delete it first or choose a different path."),
                "SettingsFileExists",
                ErrorCategory.ResourceExists,
                settingsFileInfo));
            return;
        }

        var hasBreakglassUsers = BreakglassUsers is { Length: > 0 };
        var hasBreakglassGroups = BreakglassGroups is { Length: > 0 };
        if (!hasBreakglassUsers && !hasBreakglassGroups)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("At least one -BreakglassUsers or -BreakglassGroups value must be provided."),
                "BreakglassRequired",
                ErrorCategory.InvalidArgument,
                null));
            return;
        }

        var settings = new JsonObject();
        settings["$schema"] = SchemaUrl;
        settings["tenantId"] = TenantId;

        if (hasBreakglassUsers)
        {
            var users = new JsonArray();
            foreach (var u in BreakglassUsers!)
                users.Add(u);
            settings["breakglassUsers"] = users;
        }

        if (hasBreakglassGroups)
        {
            var groups = new JsonArray();
            foreach (var g in BreakglassGroups!)
                groups.Add(g);
            settings["breakglassGroups"] = groups;
        }

        var policies = new JsonObject();

        if (PolicyFolder is not null)
        {
            var policyDir = new DirectoryInfo(PolicyFolder);
            if (!policyDir.Exists)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new DirectoryNotFoundException($"Policy folder '{policyDir.FullName}' does not exist."),
                    "PolicyFolderNotFound",
                    ErrorCategory.ObjectNotFound,
                    policyDir));
                return;
            }

            var settingsDir = settingsFileInfo.Directory!;
            var policyFiles = policyDir.GetFiles("*.json");

            foreach (var policyFile in policyFiles)
            {
                _logger.LogDebug("Reading policy file {FilePath}", policyFile.FullName);

                await using var stream = policyFile.OpenRead();
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;

                var fileStem = Path.GetFileNameWithoutExtension(policyFile.Name);
                var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var displayName = root.TryGetProperty("displayName", out var dnProp)
                    ? dnProp.GetString() ?? fileStem
                    : fileStem;
                var description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;
                var state = root.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;

                var (tag, version) = ParseDisplayName(displayName, fileStem);
                var status = NewTenant ? "report" : MapState(state);
                var policyId = NewTenant ? null : id;

                var relativePath = Path.GetRelativePath(settingsDir.FullName, policyFile.FullName)
                    .Replace('\\', '/');

                var policyEntry = new JsonObject();
                policyEntry["file"] = relativePath;
                if (policyId is not null)
                    policyEntry["id"] = policyId;
                policyEntry["name"] = displayName;
                if (!string.IsNullOrEmpty(description))
                    policyEntry["description"] = description;
                policyEntry["tag"] = tag;
                policyEntry["version"] = version;
                policyEntry["status"] = status;

                if (NewTenant)
                {
                    var wrotePlaceholders = WriteOrgArrays(policyEntry, root,
                        SourceBreakglassUsers ?? [],
                        SourceBreakglassGroups ?? []);
                    if (wrotePlaceholders)
                        WriteWarning(
                            $"Policy '{displayName}' contains user/group references from the source tenant. " +
                            "They have been written as ORG_<id> placeholders. " +
                            "Replace every ORG_ value with a valid target-tenant object ID before publishing.");
                }

                var key = SanitizeKey(tag);
                if (policies.ContainsKey(key))
                    key = SanitizeKey($"{tag}_{fileStem}");
                policies[key] = policyEntry;
            }

            _logger.LogInformation("Added {Count} {Noun} from {Folder}",
                policyFiles.Length,
                policyFiles.Length == 1 ? "policy" : "policies",
                policyDir.FullName);
        }

        settings["policies"] = policies;

        settingsFileInfo.Directory?.Create();
        var json = settings.ToJsonString(JsonOptions);
        await File.WriteAllTextAsync(settingsFileInfo.FullName, json, cancellationToken);

        _logger.LogInformation("Created settings file {FilePath}", settingsFileInfo.FullName);
        WriteObject(settingsFileInfo);
    }

    private static readonly string[] _userGroupProps =
        ["includeUsers", "excludeUsers", "includeGroups", "excludeGroups"];

    // Reads each user/group array from the policy JSON and writes it into the settings entry
    // with every ID prefixed by "ORG_". IDs that match a source-tenant breakglass entry are
    // dropped cleanly (they will be replaced by the new tenant's breakglass accounts by
    // InjectBreakglass at publish time). Returns true when at least one ORG_ placeholder was
    // written so the caller can emit a warning.
    private static bool WriteOrgArrays(
        JsonObject policyEntry,
        JsonElement root,
        string[] sourceBreakglassUsers,
        string[] sourceBreakglassGroups)
    {
        if (!root.TryGetProperty("conditions", out var conditions) ||
            !conditions.TryGetProperty("users", out var users))
            return false;

        var wrotePlaceholders = false;

        foreach (var prop in _userGroupProps)
        {
            if (!users.TryGetProperty(prop, out var arr) ||
                arr.ValueKind != JsonValueKind.Array ||
                arr.GetArrayLength() == 0)
                continue;

            // Breakglass accounts that should be silently dropped for this array type.
            var skipSet = prop is "excludeUsers" ? sourceBreakglassUsers
                        : prop is "excludeGroups" ? sourceBreakglassGroups
                        : [];

            var jsonArr = new JsonArray();
            foreach (var item in arr.EnumerateArray())
            {
                var id = item.GetString();
                if (id is null) continue;
                if (Array.IndexOf(skipSet, id) >= 0) continue;
                jsonArr.Add($"ORG_{id}");
                wrotePlaceholders = true;
            }

            if (jsonArr.Count > 0)
                policyEntry[prop] = jsonArr;
        }

        return wrotePlaceholders;
    }

    private static string MapState(string? state) => state switch
    {
        "enabled" => "enabled",
        "disabled" => "disabled",
        "enabledForReportingButNotEnforced" => "report",
        _ => "report",
    };

    // Replaces characters that are not alphanumeric, hyphen or underscore so that policy
    // keys in the settings file remain clean identifiers.
    private static string SanitizeKey(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
                chars[i] = '_';
        }
        return new string(chars);
    }

    // Matches the tag prefix before the first ": ", e.g. "CAU012-RSI" in "CAU012-RSI: Title-v1.0".
    private static readonly Regex _tagPattern = new(@"^([^:]+):\s*", RegexOptions.Compiled);
    // Matches a semantic version suffix at the end, e.g. "v1.0" in "Title-v1.0".
    private static readonly Regex _versionPattern = new(@"-(v\d+(?:\.\d+)*)$", RegexOptions.Compiled);

    // Extracts optional tag (prefix before ": ") and version (suffix matching -vX.Y) from a display
    // name like "CAU012-RSI: My Policy-v1.0". Falls back to fallbackTag / empty string when absent.
    private static (string Tag, string Version) ParseDisplayName(string displayName, string fallbackTag)
    {
        var tagMatch = _tagPattern.Match(displayName);
        var versionMatch = _versionPattern.Match(displayName);
        var tag = tagMatch.Success ? tagMatch.Groups[1].Value.Trim() : fallbackTag;
        var version = versionMatch.Success ? versionMatch.Groups[1].Value : string.Empty;
        return (tag, version);
    }
}
