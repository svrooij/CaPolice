using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace CaPolice;

internal sealed class CaPoliceAssemblyLoadContext : AssemblyLoadContext
{
    private readonly string _dependenciesDir;

    internal CaPoliceAssemblyLoadContext() : base(isCollectible: false)
    {
        _dependenciesDir = Path.Combine(
            Path.GetDirectoryName(typeof(CaPoliceAssemblyLoadContext).Assembly.Location)!,
            "Dependencies");
    }

    internal void Preload(params string[] assemblyNames)
    {
        foreach (string name in assemblyNames)
        {
            string path = Path.Combine(_dependenciesDir, $"{name}.dll");
            if (File.Exists(path))
                LoadFromAssemblyPath(path);
        }
    }

    // Resolves an assembly strictly from the Dependencies folder, returning null when it is not
    // present. Unlike LoadFromAssemblyName, this never falls back to the Default ALC, which would
    // re-trigger the Default.Resolving event and cause infinite recursion for assemblies that do
    // not exist anywhere (e.g. optional dependencies probed via Type.GetType).
    internal Assembly? ResolveFromDependencies(AssemblyName assemblyName)
    {
        string assemblyPath = Path.Combine(_dependenciesDir, $"{assemblyName.Name}.dll");
        return File.Exists(assemblyPath) ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
        => ResolveFromDependencies(assemblyName);
}
