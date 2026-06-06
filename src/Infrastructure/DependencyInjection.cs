using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using PakistanAccountingERP.Infrastructure.Data;
using PakistanAccountingERP.Infrastructure.Identity;
using PakistanAccountingERP.Infrastructure.Repositories;
using PakistanAccountingERP.Infrastructure.Services;

namespace PakistanAccountingERP.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(
                configuration.GetValue("AppSettings:SessionTimeoutMinutes", 60));
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped(typeof(ICompanyScopedRepository<>), typeof(CompanyScopedRepository<>));
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentCompanyService, CurrentCompanyService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IFbrSubmissionService, FbrSubmissionService>();
        services.AddHttpClient("FbrApi");

        return services;
    }
}
