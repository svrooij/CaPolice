using Json.Schema;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Services;

/// <summary>
/// Service for validating CaPolice settings files against the JSON schema and policies.
/// </summary>
internal class SettingsValidator : ISettingsValidator
{
    internal const string SchemaUrl = "https://raw.githubusercontent.com/svrooij/CaPolice/v0.0.6/settings/settings.schema.json";
    private readonly ILogger<SettingsValidator> _logger;

    public SettingsValidator(ILogger<SettingsValidator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<string>> ValidateSettingsFileAsync(string settingsFilePath, string? customSchemaPath = null, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        try
        {
            // Validate that the settings file exists
            if (!File.Exists(settingsFilePath))
            {
                errors.Add($"Settings file not found: {settingsFilePath}");
                return errors;
            }

            // Read the settings file
            string settingsJson;
            try
            {
                settingsJson = File.ReadAllText(settingsFilePath);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to read settings file: {ex.Message}");
                return errors;
            }

            // Load the schema
            JsonSchema schema;
            try
            {
                schema = await LoadSchemaAsync(customSchemaPath, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load schema: {ex.Message}");
                return errors;
            }

            // Parse the settings JSON
            JsonNode settingsNode;
            try
            {
                settingsNode = JsonNode.Parse(settingsJson) ?? throw new Exception("Settings JSON is empty or null");
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to parse settings file: {ex.Message}");
                return errors;
            }

            // Validate against schema
            var validationResults = schema.Evaluate(settingsNode);
            if (!validationResults.IsValid)
            {
                CollectErrors(validationResults, errors, string.Empty);
            }

            // Validate policies
            if (settingsNode is JsonObject settingsObj && settingsObj.TryGetPropertyValue("policies", out var policiesNode))
            {
                if (policiesNode is JsonObject policiesObj)
                {
                    ValidatePolicies(policiesObj, errors);
                }
            }

            if (errors.Count > 0)
            {
                _logger.LogWarning("Settings file validation found {ErrorCount} error(s)", errors.Count);
            }
            else
            {
                _logger.LogInformation("Settings file validated successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during validation");
            errors.Add($"Unexpected error: {ex.Message}");
        }

        return errors;
    }

    private async Task<JsonSchema> LoadSchemaAsync(string? customSchemaPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(customSchemaPath))
        {
            // Load from custom file
            if (!File.Exists(customSchemaPath))
                throw new FileNotFoundException($"Schema file not found: {customSchemaPath}");

            return JsonSchema.FromFile(customSchemaPath);
        }

        // Try to load from embedded resource (matches the compiled version)
        try
        {
            var assembly = typeof(SettingsValidator).Assembly;
            var resourceName = "settings.schema.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var schemaJson = await reader.ReadToEndAsync();
                _logger.LogDebug("Loaded schema from embedded resource");
                return JsonSchema.FromText(schemaJson);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load schema from embedded resource, falling back to GitHub");
        }

        // Fallback: Download from GitHub
        _logger.LogDebug("Downloading schema from GitHub");
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(SchemaUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Failed to download schema from {SchemaUrl}: {response.StatusCode}");

        var schemaJsonFromGitHub = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSchema.FromText(schemaJsonFromGitHub);
    }

    private void ValidatePolicies(JsonObject policiesObj, List<string> errors)
    {
        foreach (var (policyKey, policyNode) in policiesObj)
        {
            if (policyNode is not JsonObject policy)
            {
                errors.Add($"Policy '{policyKey}' is not a valid JSON object");
                continue;
            }

            ValidatePolicy(policyKey, policy, errors);
        }
    }

    private void ValidatePolicy(string policyKey, JsonObject policy, List<string> errors)
    {
        // Check for ORG_ placeholders in user/group arrays
        var userGroupProps = new[] { "includeUsers", "excludeUsers", "includeGroups", "excludeGroups" };

        foreach (var prop in userGroupProps)
        {
            if (policy.TryGetPropertyValue(prop, out var arrayNode) && arrayNode is JsonArray array)
            {
                foreach (var item in array)
                {
                    var value = item?.GetValue<string>();
                    if (value?.StartsWith("ORG_", StringComparison.Ordinal) == true)
                    {
                        errors.Add($"Policy '{policyKey}' contains unresolved placeholder '{value}' in '{prop}'. This is typically used during cross-tenant migration and must be replaced with an actual object ID before publishing.");
                    }
                }
            }
        }

        // Validate that policy has status field
        if (!policy.TryGetPropertyValue("status", out var statusNode) || statusNode is null)
        {
            errors.Add($"Policy '{policyKey}' is missing required 'status' property");
        }
        else
        {
            var statusValue = statusNode.GetValue<string>();
            var validStatuses = new[] { "enabled", "disabled", "report" };
            if (!validStatuses.Contains(statusValue))
            {
                errors.Add($"Policy '{policyKey}' has invalid status '{statusValue}'. Must be one of: {string.Join(", ", validStatuses)}");
            }
        }

        // Validate that policy has displayName field (typically required for reference)
        if (!policy.TryGetPropertyValue("displayName", out var displayNameNode) || string.IsNullOrEmpty(displayNameNode?.GetValue<string>()))
        {
            _logger.LogWarning($"Policy '{policyKey}' has no or empty 'displayName' property. This is recommended for documentation purposes.");
        }
    }

    private void CollectErrors(EvaluationResults results, List<string> errors, string path)
    {
        if (results.Details != null)
        {
            foreach (var detail in results.Details)
            {
                var fullPath = string.IsNullOrEmpty(path) ? detail.InstanceLocation?.ToString() ?? "root" : $"{path}/{detail.InstanceLocation}";
                CollectErrors(detail, errors, fullPath);
            }
        }

        if (!results.IsValid && (results.Details == null || results.Details.Count == 0))
        {
            errors.Add($"Schema validation error at '{path}': {results.SchemaLocation} ({results.EvaluationPath})");
        }
    }
}
