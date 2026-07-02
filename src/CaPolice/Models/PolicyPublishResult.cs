namespace CaPolice.Models;

/// <summary>
/// The result of publishing a single conditional access policy.
/// </summary>
public sealed class PolicyPublishResult
{
    /// <summary>The key identifying this policy in the settings file.</summary>
    public string SettingsKey { get; set; } = string.Empty;

    /// <summary>The human-readable display name of the policy.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The policy file path as stored in the settings file.</summary>
    public string PolicyFileName { get; set; } = string.Empty;

    /// <summary>The desired enforcement status (enabled, disabled or report).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Whether the policy was newly created or updated in the tenant.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The Graph object ID of the policy in the tenant.</summary>
    public string GraphId { get; set; } = string.Empty;
}
