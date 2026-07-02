@{
    # Script module or binary module file associated with this manifest.
    RootModule = 'CaPolice.dll'

    # Version number of this module.
    ModuleVersion = '0.0.1'

    # ID used to uniquely identify this module.
    GUID = 'd36e4c47-4835-4497-ab68-cce825adc9f5'

    # Author of this module.
    Author = 'Stephan van Rooij (@svrooij)'

    # Company or vendor that produced this module.
    CompanyName = 'Stephan van Rooij'

    Copyright = 'Stephan van Rooij 2026, licensed under GNU GPLv3'

    # Description of this module.
    Description = 'Automate your Conditional Access policies with CaPolice'

    # Minimum version of the PowerShell engine required by this module.
    # This module is build on net8.0 which requires PowerShell 7.4
    PowerShellVersion = '7.4'

    # Minimum version of the .NET Framework required by this module.
    # DotNetFrameworkVersion = '4.7.2'

    # Processor architecture (None, X86, Amd64) supported by this module.
    # ProcessorArchitecture = 'None'

    # Modules that must be imported into the global environment prior to importing this module.
    # RequiredModules = @()

    # Assemblies that must be loaded prior to importing this module.
    # RequiredAssemblies = @(
    #     "Microsoft.Extensions.Logging.Abstractions.dll",
    #     "SvR.ContentPrep.dll",
    #     "System.Buffers.dll",
    #     "System.Memory.dll",
    #     "System.Numerics.Vectors.dll",
    #     "System.Runtime.CompilerServices.Unsafe.dll"
    # )

    # Script files (.ps1) that are run in the caller's environment prior to importing this module.
    # ScriptsToProcess = @()

    # Type files (.ps1xml) that are loaded into the session prior to importing this module.
    # TypesToProcess = @()

    # Format files (.ps1xml) that are loaded into the session prior to importing this module.
    # FormatsToProcess = @()

    # Modules to import as nested modules of the module specified in RootModule/ModuleToProcess.
    # The loader registers a custom AssemblyLoadContext before the root module is loaded,
    # so its cmdlets' dependencies resolve to the bundled versions in the Dependencies folder.
    NestedModules = @('CaPolice.Loader.dll')

    # Functions to export from this module.
    # FunctionsToExport = @()

    # Cmdlets to export from this module.
    CmdletsToExport = @(
        'Connect-CaPolice',
        'Export-CaPolicePolicy',
        'New-CaPoliceSettings'
    )

    # Variables to export from this module.
    # VariablesToExport = @()

    # Aliases to export from this module.
    # AliasesToExport = @()

    # List of all files included in this module.
    FileList = @(
        "CaPolice.dll",
        "CaPolice.Loader.dll",
        "CaPolice.psd1",
        "CaPolice.psm1",
        "CaPolice.dll-Help.xml",
        "README.md"
    )

    # Private data to pass to the module specified in RootModule/ModuleToProcess.
    PrivateData = @{
        PSData = @{
            Tags = @('Conditional-Access', 'Graph')

            LicenseUri = 'https://github.com/svrooij/CaPolice/blob/main/LICENSE.txt'
            ProjectUri = 'https://github.com/svrooij/CaPolice/'
            ReleaseNotes = 'This module is still a work-in-progress. Changes might be made without notice.'
        }
    }

    # HelpInfo URI of this module.
    HelpInfoURI = 'https://github.com/svrooij/CaPolice/'
}