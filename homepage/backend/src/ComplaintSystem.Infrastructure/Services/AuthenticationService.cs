using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Options;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class AuthenticationService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext dbContext,
    IDistributedCache cache,
    ISessionService sessionService,
    IAuditService auditService,
    IEmailSender emailSender,
    ISmsSender smsSender,
    IClock clock,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<SecurityOptions> securityOptions) : IAuthenticationService
{
    private readonly FrontendOptions _frontend = frontendOptions.Value;
    private readonly SecurityOptions _security = securityOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<StudentRecoveryLookupResponse>> BeginStudentRecoveryAsync(
        StudentRecoveryLookupRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var identifier = request.Identifier.Trim();
        var normalizedReference = UniversityEmailPolicy.NormalizeReferenceNumber(identifier);
        var user = identifier.Contains('@')
            ? await userManager.FindByEmailAsync(identifier.ToLowerInvariant())
            : await dbContext.Users.SingleOrDefaultAsync(x => x.StudentReferenceNumber == normalizedReference, cancellationToken);
        if (user is null || user.IsDeleted || !await userManager.IsInRoleAsync(user, AppRoles.Student))
        {
            await DelayFailedLoginAsync(cancellationToken);
            return Result<StudentRecoveryLookupResponse>.Failure("account_not_found", "No eligible student account was found.");
        }

        var methods = new List<StudentRecoveryMethodDto>();
        if (!string.IsNullOrWhiteSpace(user.Email) && !user.Email.EndsWith("@accounts.invalid", StringComparison.OrdinalIgnoreCase))
            methods.Add(new("email", MaskEmail(user.Email)));
        if (smsSender.IsConfigured && !string.IsNullOrWhiteSpace(user.PhoneNumber))
            methods.Add(new("sms", MaskPhone(user.PhoneNumber)));
        if (methods.Count == 0)
            return Result<StudentRecoveryLookupResponse>.Failure("no_recovery_method", "No recovery method is registered for this account.");

        var recovery = new StudentPasswordRecovery { UserId = user.Id, RecoveryMethod = string.Empty };
        dbContext.StudentPasswordRecoveries.Add(recovery);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryStarted", nameof(StudentPasswordRecovery),
            recovery.Id.ToString(), true, metadata, new { availableMethods = methods.Select(x => x.Method) }, cancellationToken);
        return Result<StudentRecoveryLookupResponse>.Success(new(recovery.Id, methods));
    }

    public async Task<Result> SendStudentRecoveryCodeAsync(
        StudentRecoverySendRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var recovery = await dbContext.StudentPasswordRecoveries.SingleOrDefaultAsync(x => x.Id == request.RecoveryId, cancellationToken);
        if (recovery is null || recovery.UsedAt is not null)
            return Result.Failure("invalid_recovery", "Start a new password recovery request.");
        if (recovery.LastSentAt is not null && recovery.LastSentAt > clock.UtcNow.AddSeconds(-60))
            return Result.Failure("cooldown_active", "Please wait 60 seconds before requesting another code.");

        var user = await userManager.FindByIdAsync(recovery.UserId.ToString());
        if (user is null || !await userManager.IsInRoleAsync(user, AppRoles.Student))
            return Result.Failure("invalid_recovery", "Start a new password recovery request.");
        var method = request.Method.Trim().ToLowerInvariant();
        if (method == "email" && (string.IsNullOrWhiteSpace(user.Email) || user.Email.EndsWith("@accounts.invalid", StringComparison.OrdinalIgnoreCase)))
            return Result.Failure("method_unavailable", "Email recovery is not available.");
        if (method == "sms" && (!smsSender.IsConfigured || string.IsNullOrWhiteSpace(user.PhoneNumber)))
            return Result.Failure("method_unavailable", "Mobile recovery is not available.");
        if (method is not ("email" or "sms"))
            return Result.Failure("invalid_method", "Choose an available recovery method.");

        var code = RandomNumberGenerator.GetInt32(1000, 10000).ToString(CultureInfo.InvariantCulture);
        recovery.RecoveryMethod = method;
        recovery.OtpHash = userManager.PasswordHasher.HashPassword(user, code);
        recovery.OtpExpiresAt = clock.UtcNow.AddMinutes(5);
        recovery.LastSentAt = clock.UtcNow;
        recovery.FailedAttempts = 0;
        recovery.VerificationTokenHash = null;
        recovery.VerificationExpiresAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (method == "email")
            await emailSender.SendPasswordRecoveryCodeAsync(user.Email!, user.DisplayName, code, cancellationToken);
        else
            await smsSender.SendPasswordRecoveryCodeAsync(user.PhoneNumber!, code, cancellationToken);
        await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryCodeSent", nameof(StudentPasswordRecovery),
            recovery.Id.ToString(), true, metadata, new { method }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<StudentRecoveryVerifyResponse>> VerifyStudentRecoveryCodeAsync(
        StudentRecoveryVerifyRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var recovery = await dbContext.StudentPasswordRecoveries.SingleOrDefaultAsync(x => x.Id == request.RecoveryId, cancellationToken);
        if (recovery is null || recovery.UsedAt is not null || recovery.OtpHash is null ||
            recovery.OtpExpiresAt <= clock.UtcNow || recovery.FailedAttempts >= 5)
            return Result<StudentRecoveryVerifyResponse>.Failure("invalid_or_expired_code", "The code is invalid or expired. Request a new code.");
        var user = await userManager.FindByIdAsync(recovery.UserId.ToString());
        if (user is null) return Result<StudentRecoveryVerifyResponse>.Failure("invalid_recovery", "Start a new recovery request.");

        var valid = userManager.PasswordHasher.VerifyHashedPassword(user, recovery.OtpHash, request.Code) != PasswordVerificationResult.Failed;
        if (!valid)
        {
            recovery.FailedAttempts++;
            if (recovery.FailedAttempts >= 5) recovery.OtpExpiresAt = clock.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryCodeFailed", nameof(StudentPasswordRecovery),
                recovery.Id.ToString(), false, metadata, new { attempts = recovery.FailedAttempts }, cancellationToken);
            return Result<StudentRecoveryVerifyResponse>.Failure("invalid_code",
                recovery.FailedAttempts >= 5 ? "Too many incorrect attempts. Request a new code." : "The verification code is incorrect.");
        }

        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        recovery.VerificationTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        recovery.VerificationExpiresAt = clock.UtcNow.AddMinutes(10);
        recovery.OtpHash = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<StudentRecoveryVerifyResponse>.Success(new(rawToken));
    }

    public async Task<Result> CompleteStudentRecoveryAsync(
        StudentRecoveryCompleteRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            return Result.Failure("password_mismatch", "The new passwords do not match.");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.VerificationToken)));
        var recovery = await dbContext.StudentPasswordRecoveries.SingleOrDefaultAsync(x =>
            x.Id == request.RecoveryId && x.VerificationTokenHash == tokenHash, cancellationToken);
        if (recovery is null || recovery.UsedAt is not null || recovery.VerificationExpiresAt <= clock.UtcNow)
            return Result.Failure("invalid_recovery", "Your verified recovery session has expired.");
        var user = await userManager.FindByIdAsync(recovery.UserId.ToString());
        if (user is null || !await userManager.IsInRoleAsync(user, AppRoles.Student))
            return Result.Failure("invalid_recovery", "Start a new recovery request.");

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var passwordResult = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
        if (!passwordResult.Succeeded)
            return Result.Failure("invalid_password", string.Join(" ", passwordResult.Errors.Select(x => x.Description)));
        recovery.UsedAt = clock.UtcNow;
        recovery.VerificationTokenHash = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await sessionService.RevokeAllForUserAsync(user.Id, user.Id, "Student OTP password reset", cancellationToken);
        await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryCompleted", nameof(StudentPasswordRecovery),
            recovery.Id.ToString(), true, metadata, new { method = recovery.RecoveryMethod }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<StudentRegistrationPendingResponse>> RegisterStudentAsync(RegisterStudentRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !UniversityEmailPolicy.IsApprovedStudentEmail(email))
            return Result<StudentRegistrationPendingResponse>.Failure("invalid_university_email", "Use an approved UMaT student email address.");
        if (!UniversityEmailPolicy.IsValidReferenceNumber(request.ReferenceNumber))
            return Result<StudentRegistrationPendingResponse>.Failure("invalid_reference_number", "Invalid reference number.");
        var referenceNumber = UniversityEmailPolicy.NormalizeReferenceNumber(request.ReferenceNumber);
        var phone = request.TelephoneNumber.Trim();
        var indexNumber = request.IndexNumber.Trim().ToUpperInvariant();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (existing.AccountStatus == AccountStatus.PendingVerification && !existing.IsVerified)
                return Result<StudentRegistrationPendingResponse>.Success(new(existing.Id, MaskPhone(existing.PhoneNumber ?? phone), MaskEmail(existing.Email!)));
            return Result<StudentRegistrationPendingResponse>.Failure("registration_unavailable", "The supplied registration details cannot be used.");
        }
        if (await dbContext.Users.AnyAsync(x => x.StudentReferenceNumber == referenceNumber ||
                x.StudentIndexNumber == indexNumber || (x.PhoneNumber == phone && x.IsVerified), cancellationToken))
            return Result<StudentRegistrationPendingResponse>.Failure("registration_unavailable", "The supplied registration details cannot be used.");

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            StudentReferenceNumber = referenceNumber,
            StudentIndexNumber = indexNumber,
            PhoneNumber = phone,
            StudentDepartment = request.Department.Trim(),
            StudentLevel = request.Level.Trim(),
            Gender = request.Gender.Trim(),
            DateOfBirth = request.DateOfBirth,
            AccountStatus = AccountStatus.PendingVerification,
            IsVerified = false,
            EmailConfirmed = false,
            SecurityStamp = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            await auditService.WriteAsync(null, "AccountCreation", "ApplicationUser", null, false, metadata,
                new { errors = createResult.Errors.Select(x => x.Code) }, cancellationToken);
            return Result<StudentRegistrationPendingResponse>.Failure("registration_failed", "An account could not be created with the supplied details.");
        }
        await userManager.AddToRoleAsync(user, AppRoles.Student);
        await auditService.WriteAsync(user.Id, "AccountCreation", "ApplicationUser", user.Id.ToString(), true, metadata,
            new { role = AppRoles.Student, status = AccountStatus.PendingVerification }, cancellationToken);
        return Result<StudentRegistrationPendingResponse>.Success(new(user.Id, MaskPhone(phone), MaskEmail(email)));
    }

    public async Task<Result<StudentVerificationSendResponse>> SendStudentVerificationCodeAsync(
        StudentVerificationSendRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.PendingRegistrationId.ToString());
        if (user is null || user.IsVerified || user.AccountStatus != AccountStatus.PendingVerification)
            return Result<StudentVerificationSendResponse>.Failure("verification_unavailable", "Start a new registration or sign in.");
        var method = request.Method.Trim().ToLowerInvariant();
        if (method is not ("sms" or "email"))
            return Result<StudentVerificationSendResponse>.Failure("invalid_method", "Choose mobile number or UMaT student email.");
        if (method == "sms" && (string.IsNullOrWhiteSpace(user.PhoneNumber) || !smsSender.IsConfigured))
            return Result<StudentVerificationSendResponse>.Failure("method_unavailable", "Mobile verification is temporarily unavailable.");

        var verification = await dbContext.StudentVerifications
            .Where(x => x.StudentId == user.Id && x.UsedAt == null).OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (verification?.LockedUntil > clock.UtcNow)
            return Result<StudentVerificationSendResponse>.Failure("verification_locked", "Too many failed attempts. Try again later.");
        if (verification?.LastSentAt > clock.UtcNow.AddSeconds(-60))
            return Result<StudentVerificationSendResponse>.Failure("cooldown_active", "Please wait 60 seconds before requesting another code.");
        if (verification is null)
        {
            verification = new StudentVerification { StudentId = user.Id, Method = method, Destination = method == "sms" ? user.PhoneNumber! : user.Email! };
            dbContext.StudentVerifications.Add(verification);
        }
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        verification.Method = method;
        verification.Destination = method == "sms" ? user.PhoneNumber! : user.Email!;
        verification.CodeHash = userManager.PasswordHasher.HashPassword(user, code);
        verification.ExpiresAt = clock.UtcNow.AddMinutes(5);
        verification.LastSentAt = clock.UtcNow;
        verification.AttemptCount = 0;
        verification.ResendCount++;
        verification.LockedUntil = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (method == "sms") await smsSender.SendPasswordRecoveryCodeAsync(user.PhoneNumber!, code, cancellationToken);
        else await emailSender.SendPasswordRecoveryCodeAsync(user.Email!, user.DisplayName, code, cancellationToken);
        await auditService.WriteAsync(user.Id, "StudentVerificationCodeSent", nameof(StudentVerification), verification.Id.ToString(), true,
            metadata, new { method, verification.ResendCount }, cancellationToken);
        var masked = method == "sms" ? MaskPhone(user.PhoneNumber!) : MaskEmail(user.Email!);
        return Result<StudentVerificationSendResponse>.Success(new(method, masked, verification.ExpiresAt.Value, verification.LastSentAt.Value.AddSeconds(60)));
    }

    public async Task<Result> VerifyStudentRegistrationCodeAsync(
        StudentVerificationCodeRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.PendingRegistrationId.ToString());
        var verification = await dbContext.StudentVerifications.Where(x => x.StudentId == request.PendingRegistrationId && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (user is null || verification?.CodeHash is null || user.IsVerified)
            return Result.Failure("invalid_verification", "The verification request is invalid or expired.");
        if (verification.LockedUntil > clock.UtcNow)
            return Result.Failure("verification_locked", "Too many failed attempts. Try again later.");
        if (verification.ExpiresAt <= clock.UtcNow)
            return Result.Failure("code_expired", "OTP expired. Request a new code.");
        var valid = userManager.PasswordHasher.VerifyHashedPassword(user, verification.CodeHash, request.Code.Trim()) != PasswordVerificationResult.Failed;
        if (!valid)
        {
            verification.AttemptCount++;
            if (verification.AttemptCount >= 5) verification.LockedUntil = clock.UtcNow.AddMinutes(15);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(user.Id, "StudentVerificationCodeFailed", nameof(StudentVerification), verification.Id.ToString(), false,
                metadata, new { verification.AttemptCount }, cancellationToken);
            return Result.Failure("invalid_code", verification.AttemptCount >= 5
                ? "Too many failed attempts. Try again later." : "Invalid OTP. Please try again.");
        }
        verification.UsedAt = clock.UtcNow;
        verification.CodeHash = null;
        user.AccountStatus = AccountStatus.Active;
        user.IsVerified = true;
        user.VerifiedAt = clock.UtcNow;
        user.VerificationMethod = verification.Method.ToUpperInvariant();
        if (verification.Method == "email") user.EmailConfirmed = true;
        dbContext.Notifications.Add(new Notification { UserId = user.Id, Title = "Account Created Successfully",
            Message = "Your student account has been verified and activated successfully.", Type = NotificationType.Security, IsRead = false });
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(user.Id, "StudentAccountActivated", nameof(StudentVerification), verification.Id.ToString(), true,
            metadata, new { method = verification.Method }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
        => await LoginCoreAsync(request, metadata, false, cancellationToken);

    public async Task<Result<AuthResponse>> LoginStudentAsync(LoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
        => await LoginCoreAsync(request, metadata, true, cancellationToken);

    private async Task<Result<AuthResponse>> LoginCoreAsync(LoginRequest request, RequestMetadata metadata, bool studentOnly,
        CancellationToken cancellationToken)
    {
        var identifier = request.Identifier.Trim();
        var normalizedReference = UniversityEmailPolicy.NormalizeReferenceNumber(identifier);
        var user = identifier.Contains('@', StringComparison.Ordinal)
            ? await userManager.FindByEmailAsync(identifier.ToLowerInvariant())
            : await dbContext.Users.SingleOrDefaultAsync(x => x.StudentReferenceNumber == normalizedReference, cancellationToken);
        if (user is not null && studentOnly && !await userManager.IsInRoleAsync(user, AppRoles.Student))
        {
            user = null;
        }
        if (user is null || user.IsDeleted)
        {
            await DelayFailedLoginAsync(cancellationToken);
            await auditService.WriteAsync(null, "LoginFailure", "ApplicationUser", null, false, metadata,
                new { reason = "InvalidCredentials" }, cancellationToken);
            return InvalidLogin();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await auditService.WriteAsync(user.Id, "LoginFailure", "ApplicationUser", user.Id.ToString(), false,
                metadata, new { reason = "LockedOut" }, cancellationToken);
            return Result<AuthResponse>.Failure("account_locked", "The account is temporarily locked. Try again later.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            await auditService.WriteAsync(user.Id, "LoginFailure", "ApplicationUser", user.Id.ToString(), false,
                metadata, new { reason = "InvalidCredentials" }, cancellationToken);
            return InvalidLogin();
        }

        if (user.AccountStatus != AccountStatus.Active)
        {
            await auditService.WriteAsync(user.Id, "LoginFailure", "ApplicationUser", user.Id.ToString(), false,
                metadata, new { reason = user.AccountStatus.ToString() }, cancellationToken);
            return Result<AuthResponse>.Failure("account_unavailable", "This account is not active. Contact an administrator.");
        }

        if (!user.EmailConfirmed)
        {
            return Result<AuthResponse>.Success(new AuthResponse(false, true, false, false, null, null));
        }

        var roles = await userManager.GetRolesAsync(user);
        var requiresStrongTwoFactor = roles.Contains(AppRoles.Admin) || roles.Contains(AppRoles.SuperAdmin);
        if (requiresStrongTwoFactor)
        {
            var mode = user.TwoFactorEnabled ? "verify" : "setup";
            var challenge = await CreateChallengeAsync(user.Id, mode, request.RememberMe, cancellationToken);
            return Result<AuthResponse>.Success(new AuthResponse(
                false, false, user.TwoFactorEnabled, !user.TwoFactorEnabled, challenge, null));
        }

        return Result<AuthResponse>.Success(await CompleteSignInAsync(user, request.RememberMe, metadata, cancellationToken));
    }

    public async Task<Result<AuthResponse>> VerifyTwoFactorAsync(TwoFactorRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var challenge = await GetChallengeAsync(request.ChallengeToken, "verify", cancellationToken);
        if (challenge is null)
        {
            return Result<AuthResponse>.Failure("invalid_challenge", "The two-factor challenge has expired.");
        }
        var user = await userManager.FindByIdAsync(challenge.UserId.ToString());
        if (user is null || !user.TwoFactorEnabled || user.AccountStatus != AccountStatus.Active)
        {
            return Result<AuthResponse>.Failure("invalid_challenge", "The two-factor challenge is invalid.");
        }
        var valid = await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider,
            request.Code.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (!valid)
        {
            await userManager.AccessFailedAsync(user);
            await auditService.WriteAsync(user.Id, "LoginFailure", "ApplicationUser", user.Id.ToString(), false,
                metadata, new { reason = "InvalidTwoFactorCode" }, cancellationToken);
            return Result<AuthResponse>.Failure("invalid_two_factor", "The authentication code is invalid.");
        }
        await RemoveChallengeAsync(request.ChallengeToken, cancellationToken);
        return Result<AuthResponse>.Success(await CompleteSignInAsync(user, challenge.RememberMe, metadata, cancellationToken));
    }

    public async Task<Result<TwoFactorSetupResponse>> BeginTwoFactorSetupAsync(TwoFactorSetupRequest request, CancellationToken cancellationToken)
    {
        var challenge = await GetChallengeAsync(request.ChallengeToken, "setup", cancellationToken);
        if (challenge is null)
        {
            return Result<TwoFactorSetupResponse>.Failure("invalid_challenge", "The setup challenge has expired.");
        }
        var user = await userManager.FindByIdAsync(challenge.UserId.ToString());
        if (user is null)
        {
            return Result<TwoFactorSetupResponse>.Failure("invalid_challenge", "The setup challenge is invalid.");
        }
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }
        var issuer = Uri.EscapeDataString("UMaT Complaint System");
        var account = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString());
        var uri = $"otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6";
        return Result<TwoFactorSetupResponse>.Success(new TwoFactorSetupResponse(key!, uri));
    }

    public async Task<Result<AuthResponse>> EnableTwoFactorAsync(TwoFactorEnableRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var challenge = await GetChallengeAsync(request.ChallengeToken, "setup", cancellationToken);
        if (challenge is null)
        {
            return Result<AuthResponse>.Failure("invalid_challenge", "The setup challenge has expired.");
        }
        var user = await userManager.FindByIdAsync(challenge.UserId.ToString());
        if (user is null)
        {
            return Result<AuthResponse>.Failure("invalid_challenge", "The setup challenge is invalid.");
        }
        var valid = await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider,
            request.Code.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (!valid)
        {
            return Result<AuthResponse>.Failure("invalid_two_factor", "The authentication code is invalid.");
        }
        await userManager.SetTwoFactorEnabledAsync(user, true);
        await RemoveChallengeAsync(request.ChallengeToken, cancellationToken);
        await auditService.WriteAsync(user.Id, "TwoFactorEnabled", "ApplicationUser", user.Id.ToString(), true,
            metadata, new { }, cancellationToken);
        return Result<AuthResponse>.Success(await CompleteSignInAsync(user, challenge.RememberMe, metadata, cancellationToken));
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return Result.Failure("invalid_verification", "The verification link is invalid or expired.");
        }
        if (!TryDecodeToken(request.Token, out var token))
        {
            return Result.Failure("invalid_verification", "The verification link is invalid or expired.");
        }
        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            return Result.Failure("invalid_verification", "The verification link is invalid or expired.");
        }
        user.AccountStatus = AccountStatus.Active;
        await userManager.UpdateAsync(user);
        return Result.Success();
    }

    public async Task RequestEmailVerificationAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email.Trim().ToLowerInvariant());
        if (user is not null && !user.EmailConfirmed && !user.IsDeleted)
        {
            await SendVerificationEmailAsync(user, cancellationToken);
        }
    }

    public async Task RequestPasswordResetAsync(PasswordResetRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        await RequestPasswordResetInternalAsync(request, metadata, false, cancellationToken);
    }

    public async Task RequestAdminPasswordResetAsync(PasswordResetRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        await RequestPasswordResetInternalAsync(request, metadata, true, cancellationToken);
    }

    private async Task RequestPasswordResetInternalAsync(
        PasswordResetRequest request,
        RequestMetadata metadata,
        bool adminOnly,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null || !user.EmailConfirmed || user.IsDeleted)
        {
            await DelayFailedLoginAsync(cancellationToken);
            return;
        }
        var roles = await userManager.GetRolesAsync(user);
        var isAdministrator = roles.Any(role => role is AppRoles.DepartmentOfficer or AppRoles.Admin or AppRoles.SuperAdmin);
        if (adminOnly && !isAdministrator)
        {
            await DelayFailedLoginAsync(cancellationToken);
            return;
        }
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encoded = EncodeToken(token);
        var resetPage = isAdministrator ? "admin-reset-password.html" : "reset-password.html";
        var url = $"{_frontend.BaseUrl.TrimEnd('/')}/{resetPage}?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(encoded)}";
        await emailSender.SendPasswordResetAsync(user.Email!, user.DisplayName, url, cancellationToken);
        await auditService.WriteAsync(user.Id, "PasswordResetRequested", "ApplicationUser", user.Id.ToString(), true,
            metadata, new { }, cancellationToken);
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null || !TryDecodeToken(request.Token, out var token))
        {
            return Result.Failure("invalid_reset", "The password reset link is invalid or expired.");
        }
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return Result.Failure("invalid_reset", string.Join(" ", result.Errors.Select(x => x.Description)));
        }
        user.ForcePasswordReset = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        await userManager.UpdateAsync(user);
        await sessionService.RevokeAllForUserAsync(user.Id, user.Id, "Password reset", cancellationToken);
        await auditService.WriteAsync(user.Id, "PasswordReset", "ApplicationUser", user.Id.ToString(), true,
            metadata, new { }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result.Failure("password_mismatch", "The new passwords do not match.");
        }
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure("user_not_found", "User not found.");
        }
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Result.Failure("password_change_failed", string.Join(" ", result.Errors.Select(x => x.Description)));
        }
        user.ForcePasswordReset = false;
        await userManager.UpdateAsync(user);
        await auditService.WriteAsync(user.Id, "PasswordChanged", "ApplicationUser", user.Id.ToString(), true,
            metadata, new { }, cancellationToken);
        return Result.Success();
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
        {
            return null;
        }
        var roles = await userManager.GetRolesAsync(user);
        return ToCurrentUser(user, roles.ToArray());
    }

    public async Task<Result<CurrentUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
            return Result<CurrentUserDto>.Failure("user_not_found", "User not found.");
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return Result<CurrentUserDto>.Failure("profile_update_failed", "The profile could not be updated.");
        var roles = await userManager.GetRolesAsync(user);
        return Result<CurrentUserDto>.Success(ToCurrentUser(user, roles.ToArray()));
    }

    public async Task LogoutAsync(Guid? userId, string? sessionId, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await sessionService.RevokeAsync(sessionId, userId, "Logout", cancellationToken);
        }
        await signInManager.SignOutAsync();
        await auditService.WriteAsync(userId, "Logout", "ActiveSession", null, true, metadata, new { }, cancellationToken);
    }

    private async Task<AuthResponse> CompleteSignInAsync(ApplicationUser user, bool rememberMe, RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        await userManager.ResetAccessFailedCountAsync(user);
        var lifetime = rememberMe ? TimeSpan.FromHours(_security.RememberMeHours) : TimeSpan.FromMinutes(_security.SessionMinutes);
        var sessionId = await sessionService.CreateAsync(user.Id, metadata, lifetime, cancellationToken);
        var claims = new List<Claim> { new(ClaimNames.SessionId, sessionId) };
        if (user.DepartmentId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.DepartmentId, user.DepartmentId.Value.ToString()));
        }
        await signInManager.SignInWithClaimsAsync(user, rememberMe, claims);
        user.LastLoginAt = clock.UtcNow;
        await userManager.UpdateAsync(user);
        var roles = await userManager.GetRolesAsync(user);
        await auditService.WriteAsync(user.Id, "LoginSuccess", "ActiveSession", null, true, metadata,
            new { roles }, cancellationToken);
        return new AuthResponse(true, false, false, false, null, ToCurrentUser(user, roles.ToArray()));
    }

    private async Task SendVerificationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = EncodeToken(token);
        var url = $"{_frontend.BaseUrl.TrimEnd('/')}/verify-email.html?userId={user.Id}&token={Uri.EscapeDataString(encoded)}";
        await emailSender.SendVerificationAsync(user.Email!, user.DisplayName, url, cancellationToken);
    }

    private async Task<string> CreateChallengeAsync(Guid userId, string purpose, bool rememberMe, CancellationToken cancellationToken)
    {
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = new LoginChallenge(userId, purpose, rememberMe);
        await cache.SetStringAsync(ChallengeKey(raw), JsonSerializer.Serialize(challenge, JsonOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, cancellationToken);
        return raw;
    }

    private async Task<LoginChallenge?> GetChallengeAsync(string rawToken, string purpose, CancellationToken cancellationToken)
    {
        var json = await cache.GetStringAsync(ChallengeKey(rawToken), cancellationToken);
        var challenge = json is null ? null : JsonSerializer.Deserialize<LoginChallenge>(json, JsonOptions);
        return challenge?.Purpose == purpose ? challenge : null;
    }

    private Task RemoveChallengeAsync(string rawToken, CancellationToken cancellationToken) =>
        cache.RemoveAsync(ChallengeKey(rawToken), cancellationToken);

    private static string ChallengeKey(string raw) =>
        $"login-challenge:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))}";
    private static string MaskEmail(string email)
    {
        var parts = email.Split('@', 2);
        var visible = parts[0].Length > 0 ? parts[0][0].ToString() : string.Empty;
        return $"{visible}{new string('*', Math.Max(5, parts[0].Length - 1))}@{parts[1]}";
    }

    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var suffix = digits.Length >= 3 ? digits[^3..] : digits;
        return phone.TrimStart().StartsWith('+') ? $"+{digits[..Math.Min(3, digits.Length)]} *** *** {suffix}" : $"*** *** {suffix}";
    }

    private static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static bool TryDecodeToken(string encoded, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return true;
        }
        catch (FormatException)
        {
            token = string.Empty;
            return false;
        }
    }

    private static CurrentUserDto ToCurrentUser(ApplicationUser user, IReadOnlyCollection<string> roles) =>
        new(user.Id, user.Email?.EndsWith("@accounts.invalid", StringComparison.OrdinalIgnoreCase) == true
                ? string.Empty : user.Email ?? string.Empty,
            user.FirstName, user.LastName, roles, user.DepartmentId,
            user.StudentReferenceNumber, user.ForcePasswordReset,
            user.PhoneNumber, user.StudentDepartment, user.StudentLevel,
            user.DateOfBirth, user.CreatedAt);

    private static Result<AuthResponse> InvalidLogin() =>
        Result<AuthResponse>.Failure("invalid_credentials", "Invalid student email, reference number, or password.");

    private static Task DelayFailedLoginAsync(CancellationToken cancellationToken) => Task.Delay(150, cancellationToken);

    private sealed record LoginChallenge(Guid UserId, string Purpose, bool RememberMe);
}
