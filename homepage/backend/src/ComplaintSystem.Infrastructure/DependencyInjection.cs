using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Authorization;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Options;
using ComplaintSystem.Infrastructure.Persistence;
using ComplaintSystem.Infrastructure.Services;
using ComplaintSystem.Infrastructure.Workers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ComplaintSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<FrontendOptions>().Bind(configuration.GetSection(FrontendOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.Section))
            .Validate(x => x.AnonymousTrackingPepper.Length >= 32, "Anonymous tracking pepper must contain at least 32 characters.")
            .ValidateOnStart();
        services.AddOptions<EvidenceOptions>().Bind(configuration.GetSection(EvidenceOptions.Section)).ValidateOnStart();
        services.AddOptions<BootstrapOptions>().Bind(configuration.GetSection(BootstrapOptions.Section));
        services.AddOptions<BackupOptions>().Bind(configuration.GetSection(BackupOptions.Section));
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.Section));
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.Section));

        var useInMemory = environment.IsEnvironment("Testing") ||
            (environment.IsDevelopment() && configuration.GetValue("Database:UseInMemory", false));
        if (useInMemory)
        {
            var databaseName = configuration["Database:Name"] ?? $"complaints-{Guid.NewGuid():N}";
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        }
        else
        {
            var connectionString = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        }

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredUniqueChars = 4;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultEmailProvider;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();
        services.AddScoped<IPasswordHasher<ApplicationUser>, BcryptPasswordHasher>();

        services.Configure<DataProtectionTokenProviderOptions>(x => x.TokenLifespan = TimeSpan.FromHours(24));
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IHostEnvironment>((options, environment) =>
            {
                options.Cookie.Name = environment.IsDevelopment() ? "umat-complaints" : "__Host-umat-complaints";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            });

        if (useInMemory)
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            var redis = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
            services.AddStackExchangeRedisCache(options => options.Configuration = redis);
        }
        var keyPath = configuration["DataProtection:KeyPath"] ?? "storage/keys";
        Directory.CreateDirectory(keyPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
            .SetApplicationName("UMaTComplaintSystem")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddSingleton<IAnonymousTrackingService, AnonymousTrackingService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddHttpClient<ISmsSender, HttpSmsSender>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IMalwareScanner, SignatureOnlyMalwareScanner>();
        services.AddSingleton<IBackupProvider, PostgreSqlBackupProvider>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAdministrationService, AdministrationService>();
        services.AddScoped<IComplaintService, ComplaintService>();
        services.AddScoped<IEvidenceService, EvidenceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IReportingService, ReportingService>();
        services.AddScoped<ISystemManagementService, SystemManagementService>();
        services.AddScoped<DatabaseSeeder>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("postgresql");
        services.AddHostedService<OverdueEscalationWorker>();
        return services;
    }
}
