namespace CaPolice.Tests;

/// <summary>
/// Verifies that every cmdlet class in the CaPolice assembly is actually exposed to PowerShell.
/// The tests import the built module in a real pwsh process and, via reflection over the loaded
/// CaPolice assembly, discover every concrete type deriving from <c>PSCmdlet</c> (which includes
/// the <c>DependencyCmdlet&lt;Startup&gt;</c> base used by all commands). The cmdlet name is derived
/// from each type's <c>CmdletAttribute</c> (Verb-Noun) and cross-checked against
/// <c>Get-Command -Module CaPolice</c> and the manifest's <c>CmdletsToExport</c>. This catches a
/// command that was implemented but never wired up for export.
/// </summary>
public class CommandExposureTests
{
    /// <summary>
    /// Imports the module, then reflects over the loaded CaPolice assembly to list the Verb-Noun
    /// name of every concrete PSCmdlet-derived type. Names are emitted one per line.
    /// </summary>
    private static Task<string[]> GetReflectedCmdletNamesAsync()
    {
        var manifest = PowerShellModuleTestHelper.GetModuleManifestPath();

        // Runs inside pwsh where System.Management.Automation is available. We locate the loaded
        // CaPolice assembly, enumerate every concrete PSCmdlet-derived type, read its CmdletAttribute
        // and translate the VerbsXxx constant + Noun into the "Verb-Noun" command name.
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"Import-Module '{manifest}' -Force; " +
            "$asm = [System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies | " +
            "  Where-Object { $_.GetName().Name -eq 'CaPolice' } | Select-Object -First 1; " +
            "if (-not $asm) { throw 'CaPolice assembly not loaded' }; " +
            "$cmdletBase = [System.Management.Automation.PSCmdlet]; " +
            "$asm.GetTypes() | " +
            "  Where-Object { $cmdletBase.IsAssignableFrom($_) -and -not $_.IsAbstract } | " +
            "  ForEach-Object { " +
            "    $attr = [System.Management.Automation.CmdletAttribute]$_.GetCustomAttributes([System.Management.Automation.CmdletAttribute], $true)[0]; " +
            "    if ($attr) { \"$($attr.VerbName)-$($attr.NounName)\" } " +
            "  }";

        return PowerShellModuleTestHelper.RunPwshScriptAsync(script);
    }

    /// <summary>
    /// Returns the cmdlet names actually available at runtime from the imported module.
    /// </summary>
    private static Task<string[]> GetExportedCommandNamesAsync()
    {
        var manifest = PowerShellModuleTestHelper.GetModuleManifestPath();

        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"Import-Module '{manifest}' -Force; " +
            "Get-Command -Module CaPolice | ForEach-Object { $_.Name }";

        return PowerShellModuleTestHelper.RunPwshScriptAsync(script);
    }

    [Test]
    public async Task Reflection_FindsAtLeastOneCmdletClass()
    {
        // Guards against the reflection query silently returning nothing (which would make the
        // exposure assertions vacuously pass).
        var reflected = await GetReflectedCmdletNamesAsync();

        await Assert.That(reflected).IsNotEmpty();
    }

    [Test]
    public async Task EveryCmdletClass_IsExposedToPowerShell()
    {
        var reflected = await GetReflectedCmdletNamesAsync();
        var exported = await GetExportedCommandNamesAsync();

        var missing = reflected
            .Where(name => !exported.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"these cmdlet classes are not exposed by the module: {string.Join(", ", missing)}");
    }

    [Test]
    public async Task EveryExportedCommand_HasABackingCmdletClass()
    {
        // The reverse check: nothing is exported that does not map back to a real cmdlet class,
        // catching stale entries in CmdletsToExport.
        var reflected = await GetReflectedCmdletNamesAsync();
        var exported = await GetExportedCommandNamesAsync();

        var orphaned = exported
            .Where(name => !reflected.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(orphaned)
            .IsEmpty()
            .Because($"these exported commands have no backing cmdlet class: {string.Join(", ", orphaned)}");
    }

    [Test]
    public async Task ExportedCommands_MatchManifestCmdletsToExport()
    {
        // Ensures the manifest's static CmdletsToExport list stays in sync with what actually loads.
        var manifest = PowerShellModuleTestHelper.GetModuleManifestPath();

        var declaredScript =
            "$ErrorActionPreference = 'Stop'; " +
            $"(Import-PowerShellDataFile '{manifest}').CmdletsToExport";
        var declared = await PowerShellModuleTestHelper.RunPwshScriptAsync(declaredScript);

        var exported = await GetExportedCommandNamesAsync();

        var missingFromManifest = exported
            .Where(name => !declared.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(missingFromManifest)
            .IsEmpty()
            .Because($"these runtime commands are missing from CmdletsToExport: {string.Join(", ", missingFromManifest)}");
    }
}
