using Microsoft.Extensions.DependencyInjection;

namespace CaPolice.Core;

public static class ServiceRegistration
{
    public static void RegisterCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<Abstractions.ICredentialContainer>(Authentication.CredentialContainer.Instance);
        services.AddSingleton<Abstractions.ISettingsValidator, SettingsValidator>();
    }
}
