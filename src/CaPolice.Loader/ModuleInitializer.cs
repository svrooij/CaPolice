using System;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.Loader;

namespace CaPolice;

/// <summary>Handles module load/unload lifecycle and wires up the custom <see cref="CaPoliceAssemblyLoadContext"/>.</summary>
public sealed class ModuleInitializer : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    private static readonly CaPoliceAssemblyLoadContext s_alc = new();

    // Assemblies that PowerShell or other modules may load into the Default ALC before our
    // Resolving handler gets a chance to intercept. Pre-loading them into our private ALC
    // ensures our versions are used when the DI container or other internal code requests them.
    private static readonly string[] s_preloadAssemblies =
    [
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "System.Text.Json",
        "System.Diagnostics.DiagnosticSource",
        "System.IO.Pipelines",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
    ];

    /// <summary>Called by PowerShell when this module is imported. Registers the dependency resolver.</summary>
    public void OnImport()
    {
        s_alc.Preload(s_preloadAssemblies);
        AssemblyLoadContext.Default.Resolving += OnResolving;
    }

    /// <summary>Called by PowerShell when this module is removed. Unregisters the dependency resolver.</summary>
    public void OnRemove(PSModuleInfo psModuleInfo)
    {
        AssemblyLoadContext.Default.Resolving -= OnResolving;
    }

    private static Assembly? OnResolving(AssemblyLoadContext defaultAlc, AssemblyName assemblyName)
    {
        return s_alc.ResolveFromDependencies(assemblyName);
    }
}
