using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CaPolice.Tests;

/// <summary>
/// Verifies that the custom <c>CaPoliceAssemblyLoadContext</c> keeps the module's private
/// dependencies (CaPolice.Core, Azure.Core, etc.) out of PowerShell's Default AssemblyLoadContext.
/// These tests spawn a real PowerShell 7 process, import the built module, and inspect the
/// assemblies loaded into the Default ALC. If any isolated dependency leaks into the Default
/// ALC it can collide with versions loaded by other modules, which is exactly what the private
/// ALC is designed to prevent.
/// </summary>
public class AssemblyIsolationTests
{
    // Module-private dependencies that must NEVER appear in the Default ALC after importing the
    // module. These are shipped only inside the module's Dependencies folder and loaded exclusively
    // into the private CaPoliceAssemblyLoadContext.
    //
    // NOTE: Assemblies that PowerShell 7 ships with itself (System.Text.Json,
    // Microsoft.Extensions.DependencyInjection/Logging, etc.) are deliberately excluded: PowerShell
    // loads its own copies into the Default ALC regardless of our module, so their presence there is
    // expected and is not a leak. Only assemblies unique to this module are meaningful to assert on.
    private static readonly string[] IsolatedAssemblies =
    [
        "CaPolice.Core",
        "Azure.Core",
    ];

    /// <summary>
    /// Imports the module in a fresh pwsh process and returns the simple names of every
    /// assembly currently loaded into the Default AssemblyLoadContext.
    /// </summary>
    private static Task<string[]> GetDefaultAlcAssembliesAfterImportAsync()
    {
        var manifest = PowerShellModuleTestHelper.GetModuleManifestPath();

        // Import the module, then enumerate assemblies in the Default ALC only.
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"Import-Module '{manifest}' -Force; " +
            "[System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies | " +
            "ForEach-Object { $_.GetName().Name }";

        return PowerShellModuleTestHelper.RunPwshScriptAsync(script);
    }

    [Test]
    public async Task ImportingModule_DoesNotLeak_CaPoliceCore_IntoDefaultAlc()
    {
        var defaultAlcAssemblies = await GetDefaultAlcAssembliesAfterImportAsync();

        await Assert.That(defaultAlcAssemblies)
            .DoesNotContain("CaPolice.Core");
    }

    [Test]
    public async Task ImportingModule_DoesNotLeak_AzureCore_IntoDefaultAlc()
    {
        var defaultAlcAssemblies = await GetDefaultAlcAssembliesAfterImportAsync();

        await Assert.That(defaultAlcAssemblies)
            .DoesNotContain("Azure.Core");
    }

    [Test]
    public async Task ImportingModule_DoesNotLeak_AnyIsolatedDependency_IntoDefaultAlc()
    {
        var defaultAlcAssemblies = await GetDefaultAlcAssembliesAfterImportAsync();

        var leaked = IsolatedAssemblies
            .Where(name => defaultAlcAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(leaked)
            .IsEmpty()
            .Because($"these isolated dependencies leaked into the Default ALC: {string.Join(", ", leaked)}");
    }

    [Test]
    public async Task ImportingModule_LoadsTheModuleAssembly_IntoDefaultAlc()
    {
        // Sanity check: the module's own entry assembly (CaPolice) is expected in the Default ALC,
        // confirming the module actually imported and our isolation assertions are meaningful.
        var defaultAlcAssemblies = await GetDefaultAlcAssembliesAfterImportAsync();

        await Assert.That(defaultAlcAssemblies)
            .Contains("CaPolice");
    }
}
