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
using Google.Apis.Auth;
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
    ICurrentUser currentUser,
    IAuditService auditService,
    IEmailSender emailSender,
    ISmsSender smsSender,
    IClock clock,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<SecurityOptions> securityOptions,
    IOptions<InstitutionalLoginOptions> institutionalLoginOptions) : IAuthenticationService
{
    private readonly FrontendOptions _frontend = frontendOptions.Value;
    private readonly SecurityOptions _security = securityOptions.Value;
    private readonly InstitutionalLoginOptions _institutionalLogin = institutionalLoginOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<StudentRecoveryLookupResponse>> BeginStudentRecoveryAsync(StudentRecoveryLookupRequest request, RequestMetadata metadata, CancellationToken ct)
    {
        var id = request.Identifier.Trim();
        var user = id.Contains('@') ? await userManager.FindByEmailAsync(id.ToLowerInvariant()) :
            await dbContext.Users.SingleOrDefaultAsync(x => x.StudentReferenceNumber == UniversityEmailPolicy.NormalizeReferenceNumber(id), ct);
        if (user is null || user.IsDeleted || !await userManager.IsInRoleAsync(user, AppRoles.Student))
            return Result<StudentRecoveryLookupResponse>.Failure("account_not_found", "No eligible student account was found.");
        var methods = new List<StudentRecoveryMethodDto>();
        if (!string.IsNullOrWhiteSpace(user.Email) && !user.Email.EndsWith("@accounts.invalid", StringComparison.OrdinalIgnoreCase))
            methods.Add(new("email", MaskEmail(user.Email)));
        if (smsSender.IsConfigured && !string.IsNullOrWhiteSpace(user.PhoneNumber)) methods.Add(new("sms", MaskPhone(user.PhoneNumber)));
        if (methods.Count == 0) return Result<StudentRecoveryLookupResponse>.Failure("no_recovery_method", "No recovery method is registered for this account.");
        var recovery = new StudentPasswordRecovery { UserId = user.Id, RecoveryMethod = "" };
        dbContext.StudentPasswordRecoveries.Add(recovery);
        await dbContext.SaveChangesAsync(ct);
        await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryStarted", nameof(StudentPasswordRecovery), recovery.Id.ToString(), true, metadata, new { availableMethods = methods.Select(x => x.Method) }, ct);
        return Result<StudentRecoveryLookupResponse>.Success(new(recovery.Id, methods));
    }

    public async Task<Result> SendStudentRecoveryCodeAsync(StudentRecoverySendRequest request, RequestMetadata metadata, CancellationToken ct)
    {
        var recovery = await dbContext.StudentPasswordRecoveries.SingleOrDefaultAsync(x => x.Id == request.RecoveryId, ct);
        if (recovery is null || recovery.UsedAt is not null) return Result.Failure("invalid_recovery", "Start a new password recovery request.");
        if (recovery.LastSentAt > clock.UtcNow.AddSeconds(-60)) return Result.Failure("cooldown_active", "Please wait 60 seconds before requesting another code.");
        var user = await userManager.FindByIdAsync(recovery.UserId.ToString());
        if (user is null || !await userManager.IsInRoleAsync(user, AppRoles.Student)) return Result.Failure("invalid_recovery", "Start a new password recovery request.");
        var method = request.Method.Trim().ToLowerInvariant();
        if (method == "email" && string.IsNullOrWhiteSpace(user.Email)) return Result.Failure("method_unavailable", "Email recovery is unavailable.");
        if (method == "sms" && (!smsSender.IsConfigured || string.IsNullOrWhiteSpace(user.PhoneNumber))) return Result.Failure("method_unavailable", "Mobile recovery is unavailable.");
        if (method is not ("email" or "sms")) return Result.Failure("invalid_method", "Choose an available recovery method.");
        var code = RandomNumberGenerator.GetInt32(1000, 10000).ToString(CultureInfo.InvariantCulture);
        recovery.RecoveryMethod = method;
        recovery.OtpHash = userManager.PasswordHasher.HashPassword(user, code);
        recovery.OtpExpiresAt = clock.UtcNow.AddMinutes(5);
        recovery.LastSentAt = clock.UtcNow;
        recovery.FailedAttempts = 0;
        recovery.VerificationTokenHash = null;
        await dbContext.SaveChangesAsync(ct);
        if (method == "email") await emailSender.SendPasswordRecoveryCodeAsync(user.Email!, user.DisplayName, code, ct);
        else await smsSender.SendPasswordRecoveryCodeAsync(user.PhoneNumber!, code, ct);
        await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryCodeSent", nameof(StudentPasswordRecovery), recovery.Id.ToString(), true, metadata, new { method }, ct);
        return Result.Success();
    }

    public async Task<Result<StudentRecoveryVerifyResponse>> VerifyStudentRecoveryCodeAsync(StudentRecoveryVerifyRequest request, RequestMetadata metadata, CancellationToken ct)
    {
        var recovery = await dbContext.StudentPasswordRecoveries.SingleOrDefaultAsync(x => x.Id == request.RecoveryId, ct);
        if (recovery is null || recovery.UsedAt is not null || recovery.OtpHash is null || recovery.OtpExpiresAt <= clock.UtcNow || recovery.FailedAttempts >= 5)
            return Result<StudentRecoveryVerifyResponse>.Failure("invalid_or_expired_code", "The code is invalid or expired. Request a new code.");
        var user = await userManager.FindByIdAsync(recovery.UserId.ToString());
        if (user is null) return Result<StudentRecoveryVerifyResponse>.Failure("invalid_recovery", "Start a new recovery request.");
        if (userManager.PasswordHasher.VerifyHashedPassword(user, recovery.OtpHash, request.Code) == PasswordVerificationResult.Failed)
        {
            recovery.FailedAttempts++;
            if (recovery.FailedAttempts >= 5) recovery.OtpExpiresAt = clock.UtcNow;
            await dbContext.SaveChangesAsync(ct);
            await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryCodeFailed", nameof(StudentPasswordRecovery), recovery.Id.ToString(), false, metadata, new { attempts = recovery.FailedAttempts }, ct);
            return Result<StudentRecoveryVerifyResponse>.Failure("invalid_code", recovery.FailedAttempts >= 5 ? "Too many incorrect attempts. Request a new code." : "The verification code is incorrect.");
        }
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        recovery.VerificationTokenHash = HashRecoveryToken(token);
        recovery.VerificationExpiresAt = clock.UtcNow.AddMinutes(10);
        recovery.OtpHash = null;
        await dbContext.SaveChangesAsync(ct);
        return Result<StudentRecoveryVerifyResponse>.Success(new(token));
    }

    public async Task<Result> CompleteStudentRecoveryAsync(StudentRecoveryCompleteRequest request, RequestMetadata metadata, CancellationToken ct)
    {
        if (request.NewPassword != request.ConfirmPassword) return Result.Failure("password_mismatch", "The new passwords do not match.");
        var hash = HashRecoveryToken(request.VerificationToken);
        var recovery = await dbContext.StudentPasswordRecoveries.SingleOrDefaultAsync(x => x.Id == request.RecoveryId && x.VerificationTokenHash == hash, ct);
        if (recovery is null || recovery.UsedAt is not null || recovery.VerificationExpiresAt <= clock.UtcNow) return Result.Failure("invalid_recovery", "Your verified recovery session has expired.");
        var user = await userManager.FindByIdAsync(recovery.UserId.ToString());
        if (user is null || !await userManager.IsInRoleAsync(user, AppRoles.Student)) return Result.Failure("invalid_recovery", "Start a new recovery request.");
        var reset = await userManager.ResetPasswordAsync(user, await userManager.GeneratePasswordResetTokenAsync(user), request.NewPassword);
        if (!reset.Succeeded) return Result.Failure("invalid_password", string.Join(" ", reset.Errors.Select(x => x.Description)));
        recovery.UsedAt = clock.UtcNow;
        recovery.VerificationTokenHash = null;
        await dbContext.SaveChangesAsync(ct);
        await sessionService.RevokeAllForUserAsync(user.Id, user.Id, "Student OTP password reset", ct);
        await auditService.WriteAsync(user.Id, "StudentPasswordRecoveryCompleted", nameof(StudentPasswordRecovery), recovery.Id.ToString(), true, metadata, new { method = recovery.RecoveryMethod }, ct);
        return Result.Success();
    }

    public Task RequestAdminPasswordResetAsync(PasswordResetRequest request, RequestMetadata metadata, CancellationToken ct) =>
        RequestPasswordResetAsync(request, metadata, ct);

    public async Task<Result<StudentRegistrationPendingResponse>> RegisterStudentAsync(RegisterStudentRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        var approvedDomain = (await dbContext.SystemSettings.AsNoTracking().SingleOrDefaultAsync(
            x => x.Key == "Registration.StudentEmailDomain", cancellationToken))?.Value?.Trim().TrimStart('@') ?? "st.umat.edu.gh";
        if (email is null || !email.EndsWith($"@{approvedDomain}", StringComparison.OrdinalIgnoreCase))
        {
            return Result<StudentRegistrationPendingResponse>.Failure("invalid_university_email", "Use an approved UMaT student email address.");
        }
        if (email is not null && await userManager.FindByEmailAsync(email) is not null)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing?.AccountStatus == AccountStatus.PendingVerification && !existing.IsVerified)
                return Result<StudentRegistrationPendingResponse>.Success(new(existing.Id, MaskPhone(existing.PhoneNumber ?? ""), MaskEmail(existing.Email!)));
            return Result<StudentRegistrationPendingResponse>.Failure("registration_unavailable", "The supplied registration details cannot be used.");
        }
        if (!UniversityEmailPolicy.IsValidReferenceNumber(request.ReferenceNumber))
        {
            return Result<StudentRegistrationPendingResponse>.Failure("invalid_reference_number", "Invalid reference number.");
        }
        var referenceNumber = UniversityEmailPolicy.NormalizeReferenceNumber(request.ReferenceNumber);
        if (await dbContext.Users.AnyAsync(x => x.StudentReferenceNumber == referenceNumber, cancellationToken))
        {
            return Result<StudentRegistrationPendingResponse>.Failure("registration_unavailable", "The supplied registration details cannot be used.");
        }
        var normalizedIndex = request.IndexNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedIndex) || await dbContext.Users.AnyAsync(x =>
                x.StudentIndexNumber == normalizedIndex || (x.PhoneNumber == request.TelephoneNumber.Trim() && x.IsVerified), cancellationToken))
            return Result<StudentRegistrationPendingResponse>.Failure("registration_unavailable", "The supplied registration details cannot be used.");

        var identityEmail = email ?? $"student-{referenceNumber}@accounts.invalid";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = identityEmail,
            Email = identityEmail,
            FirstName = request.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            StudentReferenceNumber = referenceNumber,
            StudentIndexNumber = normalizedIndex,
            PhoneNumber = request.TelephoneNumber.Trim(),
            StudentDepartment = request.Department.Trim(),
            StudentLevel = request.Level.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            AccountStatus = AccountStatus.PendingVerification,
            EmailConfirmed = false,
            IsVerified = false,
            ShowWelcomeMessage = false,
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
        var student = email is null
            ? null
            : await dbContext.Students.SingleOrDefaultAsync(
                x => x.Email == email || x.ReferenceNumber == referenceNumber, cancellationToken);
        if (student is not null && student.UserId.HasValue && student.UserId != user.Id)
        {
            await userManager.DeleteAsync(user);
            return Result<StudentRegistrationPendingResponse>.Failure("registration_unavailable", "The supplied registration details cannot be used.");
        }
        if (student is null)
        {
            student = new Student
            {
                UserId = user.Id,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Email = identityEmail,
                IndexNumber = string.IsNullOrWhiteSpace(request.IndexNumber) ? null : request.IndexNumber.Trim(),
                ReferenceNumber = referenceNumber,
                Department = request.Department.Trim(),
                Programme = string.IsNullOrWhiteSpace(request.Programme) ? null : request.Programme.Trim(),
                Level = request.Level.Trim(),
                TelephoneNumber = request.TelephoneNumber.Trim(),
                DateOfBirth = request.DateOfBirth,
                Gender = request.Gender
            };
            dbContext.Students.Add(student);
        }
        else
        {
            student.UserId = user.Id;
            student.Gender = request.Gender;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(user.Id, "AccountCreation", "ApplicationUser", user.Id.ToString(), true, metadata,
            new { role = AppRoles.Student, status = AccountStatus.PendingVerification }, cancellationToken);
        return Result<StudentRegistrationPendingResponse>.Success(new(user.Id, MaskPhone(user.PhoneNumber ?? ""), MaskEmail(user.Email!)));
    }

    public async Task<Result<StudentVerificationSendResponse>> SendStudentVerificationCodeAsync(StudentVerificationSendRequest request, RequestMetadata metadata, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.PendingRegistrationId.ToString());
        if (user is null || user.IsVerified || user.AccountStatus != AccountStatus.PendingVerification)
            return Result<StudentVerificationSendResponse>.Failure("verification_unavailable", "Start a new registration or sign in.");
        var method = request.Method.Trim().ToLowerInvariant();
        if (method is not ("sms" or "email")) return Result<StudentVerificationSendResponse>.Failure("invalid_method", "Choose mobile number or UMaT student email.");
        if (method == "sms" && (string.IsNullOrWhiteSpace(user.PhoneNumber) || !smsSender.IsConfigured))
            return Result<StudentVerificationSendResponse>.Failure("method_unavailable", "Mobile verification is temporarily unavailable.");
        var verification = await dbContext.StudentVerifications.Where(x => x.StudentId == user.Id && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (verification?.LockedUntil > clock.UtcNow) return Result<StudentVerificationSendResponse>.Failure("verification_locked", "Too many failed attempts. Try again later.");
        if (verification?.LastSentAt > clock.UtcNow.AddSeconds(-60)) return Result<StudentVerificationSendResponse>.Failure("cooldown_active", "Please wait 60 seconds before requesting another code.");
        if (verification is null) { verification = new StudentVerification { StudentId=user.Id, Method=method, Destination=method=="sms"?user.PhoneNumber!:user.Email! }; dbContext.StudentVerifications.Add(verification); }
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        verification.Method=method; verification.Destination=method=="sms"?user.PhoneNumber!:user.Email!;
        verification.CodeHash=userManager.PasswordHasher.HashPassword(user,code); verification.ExpiresAt=clock.UtcNow.AddMinutes(5);
        verification.LastSentAt=clock.UtcNow; verification.AttemptCount=0; verification.ResendCount++; verification.LockedUntil=null;
        await dbContext.SaveChangesAsync(ct);
        if(method=="sms") await smsSender.SendPasswordRecoveryCodeAsync(user.PhoneNumber!,code,ct); else await emailSender.SendPasswordRecoveryCodeAsync(user.Email!,user.DisplayName,code,ct);
        await auditService.WriteAsync(user.Id,"StudentVerificationCodeSent",nameof(StudentVerification),verification.Id.ToString(),true,metadata,new{method,verification.ResendCount},ct);
        return Result<StudentVerificationSendResponse>.Success(new(method,method=="sms"?MaskPhone(user.PhoneNumber!):MaskEmail(user.Email!),verification.ExpiresAt.Value,verification.LastSentAt.Value.AddSeconds(60)));
    }

    public async Task<Result> VerifyStudentRegistrationCodeAsync(StudentVerificationCodeRequest request, RequestMetadata metadata, CancellationToken ct)
    {
        var user=await userManager.FindByIdAsync(request.PendingRegistrationId.ToString());
        var verification=await dbContext.StudentVerifications.Where(x=>x.StudentId==request.PendingRegistrationId&&x.UsedAt==null).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync(ct);
        if(user is null||verification?.CodeHash is null||user.IsVerified) return Result.Failure("invalid_verification","The verification request is invalid or expired.");
        if(verification.LockedUntil>clock.UtcNow) return Result.Failure("verification_locked","Too many failed attempts. Try again later.");
        if(verification.ExpiresAt<=clock.UtcNow) return Result.Failure("code_expired","OTP expired. Request a new code.");
        var valid=userManager.PasswordHasher.VerifyHashedPassword(user,verification.CodeHash,request.Code.Trim())!=PasswordVerificationResult.Failed;
        if(!valid){verification.AttemptCount++;if(verification.AttemptCount>=5)verification.LockedUntil=clock.UtcNow.AddMinutes(15);await dbContext.SaveChangesAsync(ct);await auditService.WriteAsync(user.Id,"StudentVerificationCodeFailed",nameof(StudentVerification),verification.Id.ToString(),false,metadata,new{verification.AttemptCount},ct);var remaining=Math.Max(0,5-verification.AttemptCount);return Result.Failure("invalid_code",verification.AttemptCount>=5?"Too many failed attempts. Verification is locked for 15 minutes.":$"Invalid verification code. Please try again. {remaining} attempt{(remaining==1?string.Empty:"s")} remaining.");}
        verification.UsedAt=clock.UtcNow;verification.CodeHash=null;user.AccountStatus=AccountStatus.Active;user.IsVerified=true;user.VerifiedAt=clock.UtcNow;user.VerificationMethod=verification.Method.ToUpperInvariant();user.EmailConfirmed=true;user.ShowWelcomeMessage=true;
        dbContext.Notifications.Add(new Notification{UserId=user.Id,Title="Account Created Successfully",Message="Your student account has been verified and activated successfully.",Type=NotificationType.Security,IsRead=false});
        await dbContext.SaveChangesAsync(ct);await auditService.WriteAsync(user.Id,"StudentAccountActivated",nameof(StudentVerification),verification.Id.ToString(),true,metadata,new{method=verification.Method},ct);return Result.Success();
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
        => await LoginCoreAsync(request, metadata, false, cancellationToken);

    public async Task<Result<AuthResponse>> LoginStudentAsync(LoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
        => await LoginCoreAsync(request, metadata, true, cancellationToken);

    public InstitutionalLoginConfigurationDto GetInstitutionalLoginConfiguration() =>
        new(!string.IsNullOrWhiteSpace(_institutionalLogin.ClientId),
            _institutionalLogin.Provider, _institutionalLogin.ClientId, _institutionalLogin.HostedDomain);

    public async Task<Result<AuthResponse>> LoginInstitutionallyAsync(
        InstitutionalLoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken)
    {
        const string provider = "Google";
        const string failureMessage = "UMaT student email authentication was unsuccessful. Please try again.";
        if (!string.Equals(_institutionalLogin.Provider, provider, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_institutionalLogin.ClientId))
            return Result<AuthResponse>.Failure("institutional_login_unavailable", failureMessage);

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [_institutionalLogin.ClientId]
                });
        }
        catch (InvalidJwtException)
        {
            await auditService.WriteAsync(null, "InstitutionalLoginFailure", nameof(ApplicationUser), null, false,
                metadata, new { reason = "InvalidIdentityToken", provider }, cancellationToken);
            return Result<AuthResponse>.Failure("institutional_authentication_failed", failureMessage);
        }

        var email = (payload.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!payload.EmailVerified ||
            !UniversityEmailPolicy.IsApprovedStudentEmail(email) ||
            !string.Equals(payload.HostedDomain, _institutionalLogin.HostedDomain,
                StringComparison.OrdinalIgnoreCase))
        {
            await auditService.WriteAsync(null, "InstitutionalLoginFailure", nameof(ApplicationUser), null, false,
                metadata, new { reason = "InvalidStudentDomain", provider }, cancellationToken);
            return Result<AuthResponse>.Failure("invalid_university_email",
                "Please use your official UMaT student email.");
        }

        var replayKey = $"institutional-token:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Credential)))}";
        if (await cache.GetStringAsync(replayKey, cancellationToken) is not null)
            return Result<AuthResponse>.Failure("institutional_authentication_failed", failureMessage);
        await cache.SetStringAsync(replayKey, "used",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            cancellationToken);

        var student = await dbContext.Students.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (student is null)
        {
            await auditService.WriteAsync(null, "InstitutionalLoginFailure", nameof(Student), null, false,
                metadata, new { reason = "StudentProfileNotFound", provider }, cancellationToken);
            return Result<AuthResponse>.Failure("student_profile_not_found",
                "Your UMaT email was verified, but no student profile was found. Please contact the administrator.");
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(x =>
            x.IdentityProvider == provider && x.ProviderSubject == payload.Subject, cancellationToken);
        user ??= await userManager.FindByEmailAsync(email);
        var created = false;
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = student.FirstName,
                LastName = student.LastName,
                PhoneNumber = student.TelephoneNumber,
                StudentReferenceNumber = student.ReferenceNumber,
                StudentDepartment = student.Department,
                StudentLevel = student.Level,
                AccountStatus = AccountStatus.Active,
                IdentityProvider = provider,
                ProviderSubject = payload.Subject,
                ProfileImageUrl = payload.Picture,
                ShowWelcomeMessage = true,
                SecurityStamp = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
            };
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return Result<AuthResponse>.Failure("institutional_authentication_failed", failureMessage);
            await userManager.AddToRoleAsync(user, AppRoles.Student);
            created = true;
        }
        else
        {
            var roles = await userManager.GetRolesAsync(user);
            if (!roles.Contains(AppRoles.Student) || roles.Any(x => x != AppRoles.Student) ||
                (student.UserId.HasValue && student.UserId != user.Id))
                return Result<AuthResponse>.Failure("institutional_authentication_failed", failureMessage);
            user.IdentityProvider = provider;
            user.ProviderSubject = payload.Subject;
            user.ProfileImageUrl = payload.Picture;
            user.EmailConfirmed = true;
            user.AccountStatus = AccountStatus.Active;
            user.FirstName = student.FirstName;
            user.LastName = student.LastName;
            await userManager.UpdateAsync(user);
        }

        student.UserId = user.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(user.Id, "InstitutionalLoginSuccess", nameof(Student), student.Id.ToString(),
            true, metadata, new { provider, created }, cancellationToken);
        return Result<AuthResponse>.Success(
            await CompleteSignInAsync(user, false, metadata, cancellationToken));
    }

    public async Task<Result<AuthResponse>> LoginStaffAsync(StaffLoginRequest request, RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var isOfficerLogin = request.LoginPage == "department_officer";
        var invalidCredentialsMessage = isOfficerLogin
            ? "Invalid Department Officer email or password."
            : "Invalid administrator email or password.";
        Result<AuthResponse> InvalidStaffLogin() => Result<AuthResponse>.Failure("invalid_staff_credentials", invalidCredentialsMessage);
        var setupComplete = await (from userRole in dbContext.UserRoles.AsNoTracking()
                                   join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                   join candidate in dbContext.Users.AsNoTracking() on userRole.UserId equals candidate.Id
                                   where role.Name == AppRoles.SuperAdmin && !candidate.IsDeleted
                                   select candidate.Id).AnyAsync(cancellationToken);
        if (!setupComplete)
        {
            await DelayFailedLoginAsync(cancellationToken);
            return InvalidStaffLogin();
        }
        var identifier = string.IsNullOrWhiteSpace(request.Identifier) ? request.Email.Trim() : request.Identifier.Trim();
        var normalizedIdentifier = identifier.ToUpperInvariant();
        var user = identifier.Contains('@', StringComparison.Ordinal)
            ? await userManager.FindByEmailAsync(identifier.ToLowerInvariant())
            : await dbContext.Users.SingleOrDefaultAsync(x =>
                x.NormalizedUserName == normalizedIdentifier || x.StaffId == normalizedIdentifier,
                cancellationToken);
        var authorized = user is not null && !user.IsDeleted &&
            (await userManager.IsInRoleAsync(user, AppRoles.DepartmentOfficer) ||
             await userManager.IsInRoleAsync(user, AppRoles.Admin) ||
             await userManager.IsInRoleAsync(user, AppRoles.SuperAdmin));
        if (!authorized)
        {
            await DelayFailedLoginAsync(cancellationToken);
            await auditService.WriteAsync(null, "LoginFailure", "ApplicationUser", null, false, metadata,
                new { reason = "InvalidStaffCredentials" }, cancellationToken);
            return InvalidStaffLogin();
        }
        if (await userManager.IsLockedOutAsync(user!) ||
            !await userManager.CheckPasswordAsync(user!, request.Password))
        {
            if (!await userManager.IsLockedOutAsync(user!)) await userManager.AccessFailedAsync(user!);
            await auditService.WriteAsync(user!.Id, "LoginFailure", "ApplicationUser", user.Id.ToString(), false,
                metadata, new { reason = "InvalidStaffCredentials" }, cancellationToken);
            return InvalidStaffLogin();
        }
        if (user!.AccountStatus != AccountStatus.Active || !user.EmailConfirmed)
        {
            await auditService.WriteAsync(user.Id, "LoginFailure", "ApplicationUser", user.Id.ToString(), false,
                metadata, new { reason = "InvalidStaffCredentials" }, cancellationToken);
            return isOfficerLogin
                ? Result<AuthResponse>.Failure("officer_account_inactive", "This Department Officer account is inactive. Please contact the Super Administrator.")
                : Result<AuthResponse>.Failure("account_inactive", "This account is inactive. Please contact the Super Administrator.");
        }
        var roles = await userManager.GetRolesAsync(user);
        var actualRole = roles.Contains(AppRoles.SuperAdmin) ? AppRoles.SuperAdmin :
            roles.Contains(AppRoles.Admin) ? AppRoles.Admin : AppRoles.DepartmentOfficer;
        var activeRole = request.LoginPage switch
        {
            "super_admin" => AppRoles.SuperAdmin,
            "admin" => AppRoles.Admin,
            "department_officer" => AppRoles.DepartmentOfficer,
            _ => actualRole
        };
        var accessAllowed = actualRole == AppRoles.SuperAdmin || actualRole == activeRole;
        if (!accessAllowed)
        {
            await auditService.WriteAsync(user.Id, "AccessRoleDenied", "ActiveSession", null, false, metadata,
                new { actualRole, requestedRole = activeRole, loginPage = request.LoginPage }, cancellationToken);
            return Result<AuthResponse>.Failure("role_access_denied", "This account is not authorised to use the selected access role.");
        }
        Guid? activeDepartmentId = null;
        if (activeRole == AppRoles.DepartmentOfficer)
        {
            if (!request.ActiveDepartmentId.HasValue || request.ActiveDepartmentId != user.DepartmentId)
            {
                await auditService.WriteAsync(user.Id, "DepartmentAccessDenied", "ActiveSession", null, false, metadata,
                    new { assignedDepartmentId = user.DepartmentId, selectedDepartmentId = request.ActiveDepartmentId }, cancellationToken);
                return Result<AuthResponse>.Failure("department_mismatch", "The selected administrative unit does not match the unit assigned to this account.");
            }
            activeDepartmentId = user.DepartmentId;
            if (activeDepartmentId.HasValue && !await dbContext.Departments.AsNoTracking()
                    .AnyAsync(x => x.Id == activeDepartmentId.Value && x.IsActive, cancellationToken))
                activeDepartmentId = null;
        }
        if (roles.Contains(AppRoles.Admin) || roles.Contains(AppRoles.SuperAdmin))
        {
            var mode = user.TwoFactorEnabled ? "verify" : "setup";
            var challenge = await CreateChallengeAsync(user.Id, mode, request.RememberMe, cancellationToken,
                request.LoginPage, actualRole, activeRole, activeDepartmentId);
            return Result<AuthResponse>.Success(new AuthResponse(
                false, false, user.TwoFactorEnabled, !user.TwoFactorEnabled, challenge, null));
        }
        return Result<AuthResponse>.Success(await CompleteSignInAsync(user, request.RememberMe, metadata,
            cancellationToken, request.LoginPage, actualRole, activeRole, activeDepartmentId));
    }

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
        if (studentOnly)
        {
            var normalizedEmail = (user.Email ?? string.Empty).Trim().ToLowerInvariant();
            var student = await dbContext.Students.SingleOrDefaultAsync(
                x => x.Email == normalizedEmail, cancellationToken);
            if (student is null)
            {
                return Result<AuthResponse>.Failure("student_profile_not_found",
                    "No student profile was found for this email. Please contact the administrator.");
            }
            if (student.UserId.HasValue && student.UserId != user.Id)
            {
                await auditService.WriteAsync(user.Id, "LoginFailure", nameof(Student), student.Id.ToString(), false,
                    metadata, new { reason = "StudentProfileOwnershipMismatch" }, cancellationToken);
                return Result<AuthResponse>.Failure("student_profile_not_found",
                    "No student profile was found for this email. Please contact the administrator.");
            }
            if (!student.UserId.HasValue)
            {
                student.UserId = user.Id;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
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
        return Result<AuthResponse>.Success(await CompleteSignInAsync(user, challenge.RememberMe, metadata,
            cancellationToken, challenge.LoginPage, challenge.ActualRole, challenge.ActiveRole,
            challenge.ActiveDepartmentId));
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
        return Result<AuthResponse>.Success(await CompleteSignInAsync(user, challenge.RememberMe, metadata,
            cancellationToken, challenge.LoginPage, challenge.ActualRole, challenge.ActiveRole,
            challenge.ActiveDepartmentId));
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
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null || !user.EmailConfirmed || user.IsDeleted)
        {
            await DelayFailedLoginAsync(cancellationToken);
            return;
        }
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encoded = EncodeToken(token);
        var url = $"{_frontend.BaseUrl.TrimEnd('/')}/reset-password.html?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(encoded)}";
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
        var student = roles.Contains(AppRoles.Student)
            ? await dbContext.Students.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            : null;
        if (roles.Contains(AppRoles.Student) && student is null) return null;
        return ToCurrentUser(user, roles.ToArray(), currentUser.ActualRole, currentUser.ActiveRole,
            currentUser.DepartmentId, student);
    }

    public async Task<Result<CurrentUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
            return Result<CurrentUserDto>.Failure("user_not_found", "User not found.");
        var normalizedStaffId = request.StaffId?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedStaffId) && await dbContext.Users
                .AnyAsync(x => x.Id != userId && x.StaffId == normalizedStaffId, cancellationToken))
            return Result<CurrentUserDto>.Failure("staff_id_exists", "This Staff ID is already assigned to another account.");
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Title = request.Title?.Trim();
        user.MiddleName = request.MiddleName?.Trim();
        user.Gender = request.Gender?.Trim();
        user.DateOfBirth = request.DateOfBirth;
        user.DepartmentId = request.DepartmentId;
        user.StaffId = string.IsNullOrWhiteSpace(normalizedStaffId) ? null : normalizedStaffId;
        user.NationalId = request.NationalId?.Trim();
        user.Position = request.Position?.Trim();
        user.OfficeLocation = request.OfficeLocation?.Trim();
        user.RecoveryEmail = request.RecoveryEmail?.Trim().ToLowerInvariant();
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.AlternativePhoneNumber = request.AlternativePhoneNumber?.Trim();
        var student = await dbContext.Students.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (student is not null)
        {
            student.FirstName = user.FirstName;
            student.LastName = user.LastName;
            student.FacultyOrSchool = request.FacultyOrSchool?.Trim();
            student.AdmissionYear = request.AdmissionYear;
            student.PersonalEmail = request.PersonalEmail?.Trim().ToLowerInvariant();
            student.ResidentialAddress = request.ResidentialAddress?.Trim();
        }
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return Result<CurrentUserDto>.Failure("profile_update_failed", "The profile could not be updated.");
        if (student is not null) await dbContext.SaveChangesAsync(cancellationToken);
        var roles = await userManager.GetRolesAsync(user);
        return Result<CurrentUserDto>.Success(ToCurrentUser(user, roles.ToArray(), student: student));
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
        CancellationToken cancellationToken, string? loginPage = null, string? actualRole = null,
        string? activeRole = null, Guid? activeDepartmentId = null)
    {
        var showWelcomeMessage = user.ShowWelcomeMessage;
        await userManager.ResetAccessFailedCountAsync(user);
        var lifetime = rememberMe ? TimeSpan.FromHours(_security.RememberMeHours) : TimeSpan.FromMinutes(_security.SessionMinutes);
        var sessionId = await sessionService.CreateAsync(user.Id, metadata, lifetime, cancellationToken);
        var roles = await userManager.GetRolesAsync(user);
        actualRole ??= roles.Contains(AppRoles.SuperAdmin) ? AppRoles.SuperAdmin :
            roles.Contains(AppRoles.Admin) ? AppRoles.Admin :
            roles.Contains(AppRoles.DepartmentOfficer) ? AppRoles.DepartmentOfficer : AppRoles.Student;
        activeRole ??= actualRole;
        var claims = new List<Claim>
        {
            new(ClaimNames.SessionId, sessionId), new(ClaimNames.ActualRole, actualRole),
            new(ClaimNames.ActiveRole, activeRole)
        };
        Student? student = null;
        if (roles.Contains(AppRoles.Student))
        {
            student = await dbContext.Students.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            if (student is null)
            {
                throw new InvalidOperationException(
                    "No student profile was found for this email. Please contact the administrator.");
            }
            claims.Add(new Claim(ClaimNames.StudentId, student.Id.ToString()));
        }
        var scopedDepartmentId = activeRole == AppRoles.DepartmentOfficer ? activeDepartmentId ?? user.DepartmentId : user.DepartmentId;
        if (scopedDepartmentId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.DepartmentId, scopedDepartmentId.Value.ToString()));
            claims.Add(new Claim(ClaimNames.AdministrativeUnitId, scopedDepartmentId.Value.ToString()));
            var administrativeUnitName = await dbContext.Departments.AsNoTracking()
                .Where(x => x.Id == scopedDepartmentId.Value)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(administrativeUnitName))
            {
                claims.Add(new Claim(ClaimNames.AdministrativeUnitName, administrativeUnitName));
            }
        }
        await signInManager.SignInWithClaimsAsync(user, rememberMe, claims);
        user.LastLoginAt = clock.UtcNow;
        user.ShowWelcomeMessage = false;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw new InvalidOperationException("The first-login state could not be persisted.");
        }
        if (showWelcomeMessage && !await dbContext.Notifications.AnyAsync(
                x => x.UserId == user.Id && x.Title == "Account Created Successfully", cancellationToken))
        {
            dbContext.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Title = "Account Created Successfully",
                Message = "Welcome to the UMaT Complaint Management System. Your account has been created successfully. You can now submit complaints, track their progress, receive announcements, and communicate securely through the system.",
                Type = NotificationType.Information,
                IsRead = false
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await auditService.WriteAsync(user.Id, "LoginSuccess", "ActiveSession", null, true, metadata,
            new { roles }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(loginPage))
        {
            var redirectDestination = activeRole == AppRoles.SuperAdmin
                ? "/dashboards/super-admin/super-admin-dashboard.html"
                : activeRole == AppRoles.Admin
                    ? "/dashboards/admin/admin-dashboard.html"
                    : "/dashboards/department-officer/department-dashboard.html";
            await auditService.WriteAsync(user.Id, "AdministrativeLoginRouting", "ActiveSession", sessionId, true,
                metadata, new
                {
                    loginPage,
                    actualRole,
                    activeRole,
                    activeDepartmentId = scopedDepartmentId,
                    redirectDestination,
                    loginTimestamp = clock.UtcNow,
                    ipAddress = metadata.IpAddress
                }, cancellationToken);
        }
        return new AuthResponse(true, false, false, false, null,
            ToCurrentUser(user, roles.ToArray(), actualRole, activeRole, scopedDepartmentId, student),
            showWelcomeMessage);
    }

    private async Task SendVerificationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = EncodeToken(token);
        var url = $"{_frontend.BaseUrl.TrimEnd('/')}/verify-email.html?userId={user.Id}&token={Uri.EscapeDataString(encoded)}";
        await emailSender.SendVerificationAsync(user.Email!, user.DisplayName, url, cancellationToken);
    }

    private async Task<string> CreateChallengeAsync(Guid userId, string purpose, bool rememberMe,
        CancellationToken cancellationToken, string? loginPage = null, string? actualRole = null,
        string? activeRole = null, Guid? activeDepartmentId = null)
    {
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = new LoginChallenge(userId, purpose, rememberMe, loginPage, actualRole, activeRole,
            activeDepartmentId);
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
    private static string HashRecoveryToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string MaskEmail(string email)
    {
        var parts = email.Split('@', 2);
        return $"{parts[0][0]}{new string('*', Math.Max(5, parts[0].Length - 1))}@{parts[1]}";
    }
    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var prefix = digits[..Math.Min(3, digits.Length)];
        var suffix = digits.Length >= 3 ? digits[^3..] : digits;
        return phone.TrimStart().StartsWith('+') ? $"+{prefix} *** *** {suffix}" : $"*** *** {suffix}";
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

    private static CurrentUserDto ToCurrentUser(ApplicationUser user, IReadOnlyCollection<string> roles,
        string? actualRole = null, string? activeRole = null, Guid? activeDepartmentId = null,
        Student? student = null) =>
        new(user.Id, user.Email?.EndsWith("@accounts.invalid", StringComparison.OrdinalIgnoreCase) == true
                ? string.Empty : user.Email ?? string.Empty,
            student?.FirstName ?? user.FirstName, student?.LastName ?? user.LastName, roles, user.DepartmentId,
            student?.ReferenceNumber ?? user.StudentReferenceNumber, user.ForcePasswordReset,
            student?.TelephoneNumber ?? user.PhoneNumber, student?.Department ?? user.StudentDepartment,
            student?.Level ?? user.StudentLevel, student?.DateOfBirth ?? user.DateOfBirth,
            user.CreatedAt, actualRole, activeRole, activeDepartmentId, student?.Id,
            student?.IndexNumber, student?.Programme, student?.Gender ?? user.Gender,
            user.Title, user.MiddleName, user.StaffId, user.NationalId, user.Position,
            user.OfficeLocation, user.RecoveryEmail, user.AlternativePhoneNumber,
            string.IsNullOrWhiteSpace(user.ProfileImageUrl) ? null : "/api/profile/photo",
            student?.FacultyOrSchool, student?.AdmissionYear, student?.PersonalEmail,
            student?.ResidentialAddress);

    private static Result<AuthResponse> InvalidLogin() =>
        Result<AuthResponse>.Failure("invalid_credentials", "Invalid student email, reference number, or password.");

    private static Task DelayFailedLoginAsync(CancellationToken cancellationToken) => Task.Delay(150, cancellationToken);

    private sealed record LoginChallenge(Guid UserId, string Purpose, bool RememberMe, string? LoginPage = null,
        string? ActualRole = null, string? ActiveRole = null, Guid? ActiveDepartmentId = null);
}
