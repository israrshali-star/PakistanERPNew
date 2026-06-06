using Microsoft.AspNetCore.Authorization;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Extensions;

public static class WebServiceExtensions
{
    public static IServiceCollection AddWebServices(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}
