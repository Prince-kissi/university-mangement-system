using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class SetupService(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager) : ISetupService
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken) =>
        new(!await HasSuperAdminAsync(cancellationToken));

    public async Task<Result<FirstSuperAdminCreatedDto>> CreateFirstSuperAdminAsync(
        CreateFirstSuperAdminRequest request, CancellationToken cancellationToken)
    {
        await SetupLock.WaitAsync(cancellationToken);
        try
        {
            if (await HasSuperAdminAsync(cancellationToken))
                return Result<FirstSuperAdminCreatedDto>.Failure("setup_complete", "Initial setup has already been completed.");
            var email = request.Email.Trim().ToLowerInvariant();
            if (await userManager.FindByEmailAsync(email) is not null)
                return Result<FirstSuperAdminCreatedDto>.Failure("email_in_use", "This email address is already registered.");
            var administratorId = string.IsNullOrWhiteSpace(request.AdministratorId)
                ? $"SA-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
                : request.AdministratorId.Trim().ToUpperInvariant();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), Email = email, UserName = email,
                FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(), EmailConfirmed = true,
                AccountStatus = AccountStatus.Active, ForcePasswordReset = false
            };
            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
                return Result<FirstSuperAdminCreatedDto>.Failure("creation_failed", string.Join(" ", created.Errors.Select(x => x.Description)));
            var role = await userManager.AddToRoleAsync(user, AppRoles.SuperAdmin);
            if (!role.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return Result<FirstSuperAdminCreatedDto>.Failure("creation_failed", "Unable to assign the Super Administrator role.");
            }
            return Result<FirstSuperAdminCreatedDto>.Success(new(administratorId));
        }
        finally { SetupLock.Release(); }
    }

    private Task<bool> HasSuperAdminAsync(CancellationToken cancellationToken) =>
        (from user in dbContext.Users.AsNoTracking()
         join userRole in dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
         join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
         where role.Name == AppRoles.SuperAdmin && !user.IsDeleted
         select user.Id).AnyAsync(cancellationToken);
}
