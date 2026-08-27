using CaPolice.Models;
using CaPolice.Services;
using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Commands;

/// <summary>
/// <para type="synopsis">Publishes conditional access policies defined in a CaPolice settings file to the connected tenant.</para>
/// <para type="description">Reads every policy entry from the settings file, loads its JSON definition from disk, strips read-only Graph properties, applies the desired state and injects the break-glass exclusions, then creates or updates the policy in the tenant via Microsoft Graph. When a policy has no id in the settings file it is created and the new id is written back. Requires Connect-CaPolice to have been run first.</para>
/// </summary>
/// <example>
/// <para type="name">Publish all policies</para>
/// <para type="description">Publish all policies defined in the settings file to the connected tenant.</para>
/// <code>Publish-CaPolicePolicy -SettingsFile ./settings.json</code>
/// </example>
/// <example>
/// <para type="name">Preview without making changes</para>
/// <para type="description">Show what would be created or updated without actually calling Graph.</para>
/// <code>Publish-CaPolicePolicy -SettingsFile ./settings.json -WhatIf</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsData.Publish, "CaPolicePolicy", SupportsShouldProcess = true)]
[OutputType(typeof(PolicyPublishResult))]
public partial class PublishCaPolicePolicyCommand : DependencyCmdlet<Startup>
{
    private const string GraphPoliciesUrl = "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies";
    private static readonly string[] RequiredScopes = ["Policy.ReadWrite.ConditionalAccess"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Read-only properties returned by Graph that must be stripped before create/update.
    private static readonly string[] ReadOnlyProperties =
    [
        "id", "createdDateTime", "modifiedDateTime", "deletedDateTime", "templateId"
    ];

    /// <summary>
    /// Path to the CaPolice settings file (settings.json). All policies defined in this file will be published.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipelineByPropertyName = true)]
    public string SettingsFile { get; set; }

    [ServiceDependency(Required = true)]
    private ILogger<PublishCaPolicePolicyCommand> _logger;

    [ServiceDependency(Required = true)]
    private Authentication.CredentialContainer _credentialContainer;

    [ServiceDependency(Required = true)]
    private ISettingsValidator _validator;

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

        var settingsFile = new FileInfo(SettingsFile);
        if (!settingsFile.Exists)
        {
            ThrowTerminatingError(new ErrorRecord(
                new FileNotFoundException($"Settings file not found: {settingsFile.FullName}"),
                "SettingsFileNotFound",
                ErrorCategory.ObjectNotFound,
                settingsFile));
            return;
        }

        // Validate settings file before proceeding
        _logger.LogInformation("Validating settings file...");
        var validationErrors = await _validator.ValidateSettingsFileAsync(settingsFile.FullName, null, cancellationToken);
        if (validationErrors.Count > 0)
        {
            _logger.LogError("Settings file validation failed with {ErrorCount} error(s). Publishing cancelled.", validationErrors.Count);
            foreach (var error in validationErrors)
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException(error),
                    "SettingsValidationFailed",
                    ErrorCategory.InvalidData,
                    settingsFile));
            }
            return;
        }
        _logger.LogInformation("Settings file validation passed.");

        var settingsJson = await File.ReadAllTextAsync(settingsFile.FullName, cancellationToken);
        using var settingsDoc = JsonDocument.Parse(settingsJson);
        var settingsRoot = settingsDoc.RootElement;

        if (!settingsRoot.TryGetProperty("policies", out var policiesElement))
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidDataException("Settings file does not contain a 'policies' object."),
                "NoPolicies",
                ErrorCategory.InvalidData,
                settingsFile));
            return;
        }

        var breakglassUsers = ReadStringArray(settingsRoot, "breakglassUsers");
        var breakglassGroups = ReadStringArray(settingsRoot, "breakglassGroups");

        if ((breakglassUsers?.Length ?? 0) == 0 && (breakglassGroups?.Length ?? 0) == 0)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidDataException("Settings file does not contain either 'breakglassUsers' or 'breakglassGroups'."),
                "NoBreakglass",
                ErrorCategory.InvalidData,
                settingsFile));
            return;
        }

        var tokenResult = await _credentialContainer.TokenCredential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(RequiredScopes), cancellationToken);

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        // We will need to write the id back for newly created policies, so build a mutable
        // copy of the settings JSON from the file content.
        var settingsNode = JsonNode.Parse(settingsJson)!.AsObject();
        var policiesNode = settingsNode["policies"]!.AsObject();

        var settingsDir = settingsFile.Directory!;
        var settingsUpdated = false;

        foreach (var policyEntry in policiesElement.EnumerateObject())
        {
            var key = policyEntry.Name;
            var entry = policyEntry.Value;

            if (!entry.TryGetProperty("file", out var fileProp))
            {
                _logger.LogWarning("Policy '{Key}' has no file property, skipping.", key);
                continue;
            }

            var relativeFile = fileProp.GetString();
            if (relativeFile is null)
            {
                _logger.LogWarning("Policy '{Key}' has an empty file property, skipping.", key);
                continue;
            }

            var policyFilePath = Path.GetFullPath(Path.Combine(settingsDir.FullName, relativeFile.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(policyFilePath))
            {
                _logger.LogWarning("Policy file '{File}' for '{Key}' does not exist, skipping.", policyFilePath, key);
                continue;
            }

            var name = entry.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? key : key;
            var existingId = entry.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var desiredStatus = entry.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "report" : "report";
            var skipBreakglass = entry.TryGetProperty("skipBreakglass", out var skipProp) && skipProp.GetBoolean();

            // Read per-policy user/group overrides and abort if any ORG_ placeholders remain.
            if (!TryReadUserGroupOverrides(entry, key, out var userGroupOverrides))
                continue;

            var policyJson = await File.ReadAllTextAsync(policyFilePath, cancellationToken);
            var policyNode = JsonNode.Parse(policyJson)!.AsObject();

            // Strip read-only properties.
            foreach (var ro in ReadOnlyProperties)
                policyNode.Remove(ro);

            // Reduce authenticationStrength to {id} only — all other fields are read-only.
            ReduceAuthenticationStrength(policyNode);

            // Remove @odata.* annotation keys Graph adds on reads but rejects on writes.
            StripODataAnnotations(policyNode);

            // Apply desired state.
            policyNode["state"] = MapStatus(desiredStatus);

            // Override user/group arrays from settings before injecting break-glass accounts.
            if (userGroupOverrides is not null)
                ApplyUserGroupOverrides(policyNode, userGroupOverrides);

            // Inject break-glass exclusions unless the policy opts out.
            if (!skipBreakglass)
            {
                InjectBreakglass(policyNode, breakglassUsers, breakglassGroups);
            }

            var isNew = string.IsNullOrEmpty(existingId);
            var action = isNew ? "New" : "Update";
            var target = isNew ? "Create policy" : $"Update policy {existingId}";

            if (!ShouldProcess(name, target))
                continue;

            string graphId;

            if (isNew)
            {
                _logger.LogInformation("Creating new policy '{Name}' (key: {Key})", name, key);
                graphId = await CreatePolicyAsync(httpClient, policyNode, cancellationToken);

                // Write the new id back into the settings node so it can be persisted.
                policiesNode[key]!.AsObject()["id"] = graphId;
                settingsUpdated = true;

                _logger.LogInformation("Created policy '{Name}' with id {Id}", name, graphId);
            }
            else
            {
                _logger.LogInformation("Updating policy '{Name}' (id: {Id})", name, existingId);
                await UpdatePolicyAsync(httpClient, existingId!, policyNode, cancellationToken);
                graphId = existingId!;
                _logger.LogInformation("Updated policy '{Name}'", name);
            }

            WriteObject(new PolicyPublishResult
            {
                SettingsKey = key,
                Name = name,
                PolicyFileName = relativeFile,
                Status = desiredStatus,
                Action = action,
                GraphId = graphId,
            });
        }

        // Persist updated ids back to the settings file.
        if (settingsUpdated)
        {
            var updatedJson = settingsNode.ToJsonString(JsonOptions);
            await File.WriteAllTextAsync(settingsFile.FullName, updatedJson, cancellationToken);
            _logger.LogInformation("Settings file updated with new policy ids.");
        }
    }

    private static async Task<string> CreatePolicyAsync(HttpClient httpClient, JsonObject policyNode, CancellationToken cancellationToken)
    {
        var body = new StringContent(policyNode.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(GraphPoliciesUrl, body, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("id", out var idProp))
            throw new InvalidOperationException("Graph did not return an id for the newly created policy.");

        return idProp.GetString()
            ?? throw new InvalidOperationException("Graph returned a null id for the newly created policy.");
    }

    private static async Task UpdatePolicyAsync(HttpClient httpClient, string id, JsonObject policyNode, CancellationToken cancellationToken)
    {
        var url = $"{GraphPoliciesUrl}/{id}";
        var body = new StringContent(policyNode.ToJsonString(), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = body };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // Ensures break-glass users and groups are present in conditions>users>excludeUsers / excludeGroups.
    private static void InjectBreakglass(JsonObject policyNode, string[]? breakglassUsers, string[]? breakglassGroups)
    {
        var conditions = policyNode["conditions"]?.AsObject() ?? new JsonObject();
        var users = conditions["users"]?.AsObject() ?? new JsonObject();

        if (breakglassUsers?.Length > 0)
        {
            var excludeUsers = users["excludeUsers"]?.AsArray() ?? new JsonArray();
            foreach (var u in breakglassUsers)
            {
                if (!ContainsValue(excludeUsers, u))
                    excludeUsers.Add(u);
            }
            users["excludeUsers"] = excludeUsers;
        }

        if (breakglassGroups?.Length > 0)
        {
            var excludeGroups = users["excludeGroups"]?.AsArray() ?? new JsonArray();
            foreach (var g in breakglassGroups)
            {
                if (!ContainsValue(excludeGroups, g))
                    excludeGroups.Add(g);
            }
            users["excludeGroups"] = excludeGroups;
        }
        conditions["users"] = users;
        policyNode["conditions"] = conditions;
    }

    private static bool ContainsValue(JsonArray array, string value)
    {
        foreach (var item in array)
        {
            if (item?.GetValue<string>() == value)
                return true;
        }
        return false;
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
            return [];

        var list = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            var value = item.GetString();
            if (value is not null)
                list.Add(value);
        }
        return [.. list];
    }

    // Reduces grantControls.authenticationStrength to {id} only.
    // Graph only needs the id to reference a built-in or custom authentication strength;
    // all other fields (displayName, policyType, allowedCombinations, etc.) are read-only
    // and cause a 400 Bad Request on POST/PATCH.
    private static void ReduceAuthenticationStrength(JsonObject policyNode)
    {
        if (policyNode["grantControls"] is not JsonObject grantControls)
            return;
        if (grantControls["authenticationStrength"] is not JsonObject authStrength)
            return;

        var id = authStrength["id"]?.GetValue<string>();
        grantControls["authenticationStrength"] = id is not null
            ? new JsonObject { ["id"] = id }
            : null;
    }

    // Recursively removes all OData annotation properties (any key containing "@odata.")
    // from the JSON tree. Graph adds these as metadata on reads but rejects them on writes.
    private static void StripODataAnnotations(JsonObject obj)
    {
        var keysToRemove = new List<string>();
        foreach (var (key, _) in obj)
        {
            if (key.Contains("@odata.", StringComparison.OrdinalIgnoreCase))
                keysToRemove.Add(key);
        }
        foreach (var key in keysToRemove)
            obj.Remove(key);

        foreach (var (_, value) in obj)
        {
            if (value is JsonObject nested)
                StripODataAnnotations(nested);
            else if (value is JsonArray array)
                StripODataAnnotations(array);
        }
    }

    private static void StripODataAnnotations(JsonArray array)
    {
        foreach (var item in array)
        {
            if (item is JsonObject obj)
                StripODataAnnotations(obj);
        }
    }

    private static readonly string[] _userGroupProps =
        ["includeUsers", "excludeUsers", "includeGroups", "excludeGroups"];

    // Reads the four user/group override arrays from a settings policy entry.
    // Returns false (skipping the policy) if any value starts with ORG_, which indicates
    // a placeholder left over from a cross-tenant migration that has not been resolved yet.
    private bool TryReadUserGroupOverrides(
        JsonElement entry,
        string key,
        out Dictionary<string, string[]>? overrides)
    {
        overrides = null;
        Dictionary<string, string[]>? result = null;

        foreach (var prop in _userGroupProps)
        {
            if (!entry.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;

            var values = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                var value = item.GetString();
                if (value is null) continue;

                if (value.StartsWith("ORG_", StringComparison.Ordinal))
                {
                    WriteError(new ErrorRecord(
                        new InvalidDataException(
                            $"Policy '{key}' has an unresolved ORG_ placeholder '{value}' in {prop}. " +
                            "Replace it with a valid target-tenant object ID before publishing."),
                        "UnresolvedOrgPlaceholder",
                        ErrorCategory.InvalidData,
                        key));
                    return false;
                }
                values.Add(value);
            }

            result ??= new Dictionary<string, string[]>();
            result[prop] = [.. values];
        }

        overrides = result;
        return true;
    }

    // Replaces conditions.users include/exclude arrays in the policy JSON with the values
    // from the settings entry. Only arrays explicitly present in the settings are overridden;
    // absent properties are left unchanged from the policy JSON.
    private static void ApplyUserGroupOverrides(
        JsonObject policyNode,
        Dictionary<string, string[]> overrides)
    {
        var conditions = policyNode["conditions"]?.AsObject() ?? new JsonObject();
        policyNode["conditions"] = conditions;
        var users = conditions["users"]?.AsObject() ?? new JsonObject();
        conditions["users"] = users;

        foreach (var (prop, values) in overrides)
        {
            var arr = new JsonArray();
            foreach (var v in values)
                arr.Add(v);
            users[prop] = arr;
        }
    }

    // Maps the settings status value to the Graph state string.
    private static string MapStatus(string status) => status switch
    {
        "enabled" => "enabled",
        "disabled" => "disabled",
        "report" => "enabledForReportingButNotEnforced",
        _ => "enabledForReportingButNotEnforced",
    };
}
