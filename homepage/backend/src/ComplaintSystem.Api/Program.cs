using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ComplaintSystem.Api.Filters;
using ComplaintSystem.Api.Middleware;
using ComplaintSystem.Api.Services;
using ComplaintSystem.Application;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "UMaTComplaintSystem")
    .WriteTo.Console());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<FluentValidationFilter>();
builder.Services.AddScoped<AntiforgeryFilter>();
builder.Services.AddControllers(options =>
    {
        options.Filters.AddService<AntiforgeryFilter>(order: -2000);
        options.Filters.AddService<FluentValidationFilter>(order: -1000);
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "UMaT Student Complaint Management API", Version = "v1" });
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ActiveRole:DepartmentOfficer", policy => policy.RequireAssertion(context =>
        context.User.IsInRole(AppRoles.DepartmentOfficer) ||
        context.User.HasClaim(ClaimNames.ActiveRole, AppRoles.DepartmentOfficer)))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
}));

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "umat-antiforgery" : "__Host-umat-antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SuppressXFrameOptionsHeader = true;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var strict = context.Request.Path.StartsWithSegments("/api/auth") ||
                     context.Request.Path.StartsWithSegments("/api/anonymous-complaints");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = strict ? 20 : 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});

var app = builder.Build();

var staticFilesPath = app.Configuration["Frontend:StaticFilesPath"];
PhysicalFileProvider? frontendFiles = null;
if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(staticFilesPath))
{
    var frontendRoot = Path.GetFullPath(staticFilesPath, app.Environment.ContentRootPath);
    frontendFiles = new PhysicalFileProvider(frontendRoot);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = frontendFiles,
        DefaultFileNames = { "homepage.html" }
    });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendFiles });
}

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
if (app.Configuration.GetValue("Security:RequireHttps", !app.Environment.IsDevelopment()))
{
    app.UseHttpsRedirection();
}
app.UseCors("Frontend");
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<ForcedPasswordChangeMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
if (frontendFiles is not null)
{
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "API endpoint not found",
                detail = $"No API endpoint is registered for {context.Request.Path}."
            });
            return;
        }

        var homepage = frontendFiles.GetFileInfo("homepage.html");
        if (!homepage.Exists)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Page not found.");
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(homepage);
    }).AllowAnonymous();
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (dbContext.Database.IsRelational()) await dbContext.Database.MigrateAsync();
    else await dbContext.Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

await app.RunAsync();

public partial class Program;
