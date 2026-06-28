#Requires -Module Pester
[CmdletBinding()]
param()

BeforeAll {
    $ModuleRoot = "$PSScriptRoot/../src/CaPolice/bin/Release/net8.0"
    $ManifestPath = "$ModuleRoot/CaPolice.psd1"

    # Fail early with a clear message if the build output is missing
    if (-not (Test-Path $ManifestPath)) {
        throw "Module manifest not found at '$ManifestPath'. Did you run 'dotnet build -c Release'?"
    }

    Import-Module $ManifestPath -Force -ErrorAction Stop
}

AfterAll {
    Remove-Module CaPolice -ErrorAction SilentlyContinue
}

Describe 'Module manifest' {
    It 'passes Test-ModuleManifest' {
        $ManifestPath = "$PSScriptRoot/../src/CaPolice/bin/Release/net8.0/CaPolice.psd1"
        Test-ModuleManifest -Path $ManifestPath | Should -Not -BeNullOrEmpty
    }

    It 'has a valid LicenseUri (not LisenceUri)' {
        $ManifestPath = "$PSScriptRoot/../src/CaPolice/bin/Release/net8.0/CaPolice.psd1"
        $manifest = Test-ModuleManifest -Path $ManifestPath
        $manifest.LicenseUri | Should -Not -BeNullOrEmpty
    }

    It 'declares a non-empty CmdletsToExport' {
        $ManifestPath = "$PSScriptRoot/../src/CaPolice/bin/Release/net8.0/CaPolice.psd1"
        $manifest = Test-ModuleManifest -Path $ManifestPath
        $manifest.ExportedCmdlets.Count | Should -BeGreaterThan 0
    }
}

Describe 'Exported cmdlets' {
    BeforeAll {
        $module = Get-Module CaPolice
        $exported = $module.ExportedCmdlets.Keys
        $declared = (Test-ModuleManifest "$PSScriptRoot/../src/CaPolice/bin/Release/net8.0/CaPolice.psd1").ExportedCmdlets.Keys
    }

    It 'exports Connect-CaPolice' {
        Get-Command Connect-CaPolice -Module CaPolice | Should -Not -BeNullOrEmpty
    }

    It 'has no cmdlets exported at runtime that are missing from CmdletsToExport in the manifest' {
        $missing = $exported | Where-Object { $_ -notin $declared }
        $missing | Should -BeNullOrEmpty -Because "every runtime cmdlet must be listed in CmdletsToExport"
    }

    It 'has no cmdlets in CmdletsToExport that are missing at runtime' {
        $ghost = $declared | Where-Object { $_ -notin $exported }
        $ghost | Should -BeNullOrEmpty -Because "every declared cmdlet must actually be loadable"
    }
}

Describe 'Connect-CaPolice parameter sets' {
    BeforeAll {
        $cmd = Get-Command Connect-CaPolice -Module CaPolice
    }

    It 'has a DefaultCredentials parameter set' {
        $cmd.ParameterSets.Name | Should -Contain 'DefaultCredentials'
    }

    It 'has a GitHub parameter set' {
        $cmd.ParameterSets.Name | Should -Contain 'GitHub'
    }

    It 'has a ManagedIdentity parameter set' {
        $cmd.ParameterSets.Name | Should -Contain 'ManagedIdentity'
    }

    It 'defaults to DefaultCredentials' {
        $cmd.DefaultParameterSet | Should -Be 'DefaultCredentials'
    }
}
