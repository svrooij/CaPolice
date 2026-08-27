using CaPolice.Services;
using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using System;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace CaPolice.Commands;

/// <summary>
/// <para type="synopsis">Validates a CaPolice settings file against the JSON schema and policies.</para>
/// <para type="description">This cmdlet validates a CaPolice settings file against the official JSON schema and checks all policies contained within for common issues such as unresolved ORG_ placeholders and other policy validation errors. All validation errors are reported and the cmdlet returns $false if any errors are found.</para>
/// </summary>
/// <example>
/// <para type="name">Validate a settings file</para>
/// <para type="description">Validate a settings file against the schema and all contained policies.</para>
/// <code>Test-CaPoliceSettings -SettingsFile ./settings.json</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsDiagnostic.Test, "CaPoliceSettings")]
[OutputType(typeof(bool))]
public partial class ValidateCaPoliceSettingsCommand : DependencyCmdlet<Startup>
{
    private const string SchemaUrl = "https://raw.githubusercontent.com/svrooij/CaPolice/v0.0.6/settings/settings.schema.json";

    /// <summary>
    /// Path to the settings file to validate.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipelineByPropertyName = true)]
    public string SettingsFile { get; set; }

    /// <summary>
    /// Optional path to a custom JSON schema file. If not specified, the embedded schema is used (matching the compiled version).
    /// </summary>
    [Parameter(
        Mandatory = false)]
    public string SettingsSchema { get; set; }

    [ServiceDependency(Required = true)]
    private ILogger<ValidateCaPoliceSettingsCommand> _logger;

    [ServiceDependency(Required = true)]
    private ISettingsValidator _validator;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            var errors = await _validator.ValidateSettingsFileAsync(SettingsFile, SettingsSchema, cancellationToken);

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    WriteError(new ErrorRecord(
                        new InvalidOperationException(error),
                        "ValidationError",
                        ErrorCategory.InvalidData,
                        SettingsFile));
                }
                WriteObject(false);
            }
            else
            {
                _logger.LogInformation("Settings file validated successfully");
                WriteObject(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during validation");
            WriteError(new ErrorRecord(
                new InvalidOperationException($"Unexpected error: {ex.Message}", ex),
                "UnexpectedError",
                ErrorCategory.NotSpecified,
                SettingsFile));
            WriteObject(false);
        }
    }
}
