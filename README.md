# CaPolice

[![PowerShell Gallery](https://img.shields.io/powershellgallery/v/CaPolice?label=PowerShell%20Gallery)](https://www.powershellgallery.com/packages/CaPolice)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE.txt)
[![GitHub](https://img.shields.io/badge/source-GitHub-181717?logo=github)](https://github.com/svrooij/CaPolice)
[![GitHub Issues](https://img.shields.io/github/issues/svrooij/CaPolice)](https://github.com/svrooij/CaPolice/issues)

Automate your Entra ID Conditional Access policies with CaPolice — a PowerShell module that lets you export, version-control and deploy conditional access policies as code.

## Requirements

- PowerShell 7.4 or later
- .NET 8 runtime

## Installation

```powershell
Install-Module -Name CaPolice -Repository PSGallery
```

Or, to install for the current user only:

```powershell
Install-Module -Name CaPolice -Repository PSGallery -Scope CurrentUser
```

## Quick start

```powershell
# 1. Authenticate (interactive browser fallback on developer machines)
Connect-CaPolice -UseDefaultCredentials
# 1b. For use in GitHub Actions, use the -GitHub switch, which will handle the authentication using GitHub federated credentials

# 2. Export all policies from your tenant to JSON files
Export-CaPolicePolicy -OutputPath ./Policies

# 3. Create a settings file that references the exported policies
New-CaPoliceSettings -SettingsFile ./settings.json `
    -TenantId "00000000-0000-0000-0000-000000000000" `
    -BreakglassUsers "breakglass-user-object-id" `
    -PolicyFolder ./Policies
# 3b. If deploying to a new tenant, use the -NewTenant switch to omit policy IDs and force all statuses to 'report'

# 4. Modify your settings accordingly, using Visual Studio Code which validates the settings

# 5. Deploy the policies to your (new) tenant
Publish-CaPolicePolicy -SettingsFile ./settings.json
```

## Commands

### `Connect-CaPolice`

Authenticates to Microsoft Graph. Three authentication methods are supported:

| Parameter | Description |
|---|---|
| `-UseDefaultCredentials` | Uses `DefaultAzureCredential` — falls back to an interactive browser on developer machines. |
| `-UseManagedIdentity` | Uses a managed identity (for workloads running in Azure). |
| `-Github` | Uses GitHub Actions workload identity federation. |

```powershell
# Interactive / developer machine
Connect-CaPolice -UseDefaultCredentials

# GitHub Actions
Connect-CaPolice -Github

# Managed identity
Connect-CaPolice -UseManagedIdentity
```

### `Export-CaPolicePolicy`

Retrieves all conditional access policies from the connected tenant and writes each one to a JSON file. Requires `Connect-CaPolice` to be run first.

| Parameter | Description |
|---|---|
| `-OutputPath` | Directory to write JSON files into. Created if it does not exist. |
| `-FileNameFormat` | File name template. Supports `{id}`, `{displayName}`, `{tag}` and `{version}`. Defaults to `{id}.json`. |
| `-Force` | Overwrite existing files. Without this switch, existing files are skipped. |

```powershell
# Export using policy IDs as file names
Export-CaPolicePolicy -OutputPath ./Policies

# Export using tag subdirectories
Export-CaPolicePolicy -OutputPath ./Policies -FileNameFormat "{tag}/{id}-{version}.json"

# Overwrite existing files
Export-CaPolicePolicy -OutputPath ./Policies -Force
```

### `New-CaPoliceSettings`

Creates a new `settings.json` file for CaPolice. Throws an error if the file already exists. When `-PolicyFolder` is specified, all `*.json` policy files in that folder are imported as policy entries; the tag and version are parsed from each policy's `displayName` following the `"TAG: Title-vX.Y"` convention.

| Parameter | Description |
|---|---|
| `-SettingsFile` | Path to the settings file to create. |
| `-TenantId` | Entra ID tenant ID. |
| `-BreakglassUsers` | One or more break-glass user object IDs excluded from all policies. |
| `-BreakglassGroups` | One or more break-glass group object IDs excluded from all policies. |
| `-PolicyFolder` | Folder of exported policy JSON files to import into the settings. |
| `-NewTenant` | Omit policy IDs and force all statuses to `report`. Use when deploying to a new tenant. |

```powershell
# Minimal settings file
New-CaPoliceSettings -SettingsFile ./settings.json `
    -TenantId "00000000-0000-0000-0000-000000000000" `
    -BreakglassUsers "breakglass-user-object-id"

# Import policies from an exported folder for a new tenant
New-CaPoliceSettings -SettingsFile ./settings.json `
    -TenantId "00000000-0000-0000-0000-000000000000" `
    -BreakglassGroups "breakglass-group-object-id" `
    -PolicyFolder ./Policies `
    -NewTenant
```

### `Test-CaPoliceSettings`

Validates a CaPolice settings file against the JSON schema and checks all policies for common issues such as unresolved placeholders and invalid status values. The validation results are returned as `$true` (valid) or `$false` (invalid), with all errors reported via the error pipeline.

| Parameter | Description |
|---|---|
| `-SettingsFile` | Path to the settings file to validate. |
| `-SettingsSchema` | Optional path to a custom JSON schema file. If not specified, the embedded schema matching the compiled version is used. |

```powershell
# Validate a settings file
Test-CaPoliceSettings -SettingsFile ./settings.json

# Validate with a custom schema
Test-CaPoliceSettings -SettingsFile ./settings.json -SettingsSchema ./custom-schema.json
```

### `Publish-CaPolicePolicy`

Publishes conditional access policies defined in a CaPolice settings file to the connected tenant. Reads each policy entry from the settings file, loads its JSON definition from disk, validates the settings, strips read-only Graph properties, applies the desired state and injects break-glass exclusions, then creates or updates the policy in the tenant via Microsoft Graph. When a policy has no id in the settings file it is created and the new id is written back. Requires `Connect-CaPolice` to be run first.

| Parameter | Description |
|---|---|
| `-SettingsFile` | Path to the CaPolice settings file. All policies defined in this file will be published. |
| `-WhatIf` | Preview what would be created or updated without actually calling Graph. |

```powershell
# Publish all policies
Publish-CaPolicePolicy -SettingsFile ./settings.json

# Preview without making changes
Publish-CaPolicePolicy -SettingsFile ./settings.json -WhatIf
```

## Links

- [PowerShell Gallery](https://www.powershellgallery.com/packages/CaPolice)
- [GitHub Repository](https://github.com/svrooij/CaPolice)
- [License (GNU GPLv3)](LICENSE.txt)
