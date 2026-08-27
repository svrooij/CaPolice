using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Services;

/// <summary>
/// Service for validating CaPolice settings files against the JSON schema and policies.
/// </summary>
public interface ISettingsValidator
{
    /// <summary>
    /// Validates a CaPolice settings file against the JSON schema and performs policy validation.
    /// </summary>
    /// <param name="settingsFilePath">Path to the settings file to validate.</param>
    /// <param name="customSchemaPath">Optional path to a custom schema file. If not provided, the embedded schema is used.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of validation errors. Empty list indicates validation passed.</returns>
    Task<List<string>> ValidateSettingsFileAsync(string settingsFilePath, string? customSchemaPath = null, CancellationToken cancellationToken = default);
}
