using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Persistence;
using ComplaintSystem.Infrastructure.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ComplaintSystem.IntegrationTests;

public sealed class ComplaintWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"complaints-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                ["ConnectionStrings:Redis"] = "unused:6379,abortConnect=false",
                ["Frontend:BaseUrl"] = "https://localhost",
                ["Security:AnonymousTrackingPepper"] = "INTEGRATION_TEST_ONLY_RANDOM_PEPPER_1234567890",
                ["Security:RequireHttps"] = "false",
                ["Database:MigrateOnStartup"] = "false",
                ["Database:Provider"] = "InMemory",
                ["Database:Name"] = _databaseName,
                ["Evidence:StoragePath"] = Path.Combine(Path.GetTempPath(), "umat-complaint-tests", _databaseName),
                ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "umat-complaint-tests", _databaseName, "keys")
            }));
        builder.ConfigureServices(services =>
        {
            var worker = services.SingleOrDefault(x => x.ImplementationType == typeof(OverdueEscalationWorker));
            if (worker is not null) services.Remove(worker);
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<FakeEmailSender>());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync().GetAwaiter().GetResult();
        return host;
    }
}
