using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using Svrooij.PowerShell.DI.Logging;
using System;

namespace CaPolice;
/// <inheritdoc/>
public class Startup : PsStartup
{
    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<Authentication.CredentialContainer>();
    }

    /// <inheritdoc/>
    public override Action<PowerShellLoggerConfiguration> ConfigurePowerShellLogging()
    {
        return builder =>
        {
            builder.DefaultLevel = LogLevel.Debug;
            builder.LogLevel.Add("System.Net.Http.HttpClient", LogLevel.Warning);
            builder.LogLevel.Add("System.Net.Http.HttpClient.GraphClientFactory.LogicalHandler", LogLevel.Warning);
            builder.IncludeCategory = true;
            builder.StripNamespace = true;
        };
    }
}
