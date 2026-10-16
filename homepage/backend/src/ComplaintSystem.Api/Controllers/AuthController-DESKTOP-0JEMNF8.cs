using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(IAuthenticationService authenticationService, IAntiforgery antiforgery) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    public ActionResult<CsrfTokenResponse> Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new CsrfTokenResponse(tokens.RequestToken!));
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [HttpPost("students/register")]
    public async Task<ActionResult<StudentRegistrationPendingResponse>> Register(RegisterStudentRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.RegisterStudentAsync(request, RequestMetadata, cancellationToken);
        return result.Succeeded
            ? Accepted(result.Value)
            : FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("students/send-verification-code")]
    [HttpPost("students/resend-verification-code")]
    [HttpPost("students/change-verification-method")]
    public async Task<ActionResult<StudentVerificationSendResponse>> SendStudentVerificationCode(StudentVerificationSendRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.SendStudentVerificationCodeAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("students/verify-code")]
    public async Task<IActionResult> VerifyStudentRegistrationCode(StudentVerificationCodeRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.VerifyStudentRegistrationCodeAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.LoginAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("student/login")]
    public async Task<ActionResult<AuthResponse>> StudentLogin(LoginRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.LoginStudentAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpGet("student/institutional/config")]
    public ActionResult<InstitutionalLoginConfigurationDto> InstitutionalLoginConfiguration() =>
        Ok(authenticationService.GetInstitutionalLoginConfiguration());

    [AllowAnonymous]
    [HttpPost("student/institutional")]
    public async Task<ActionResult<AuthResponse>> InstitutionalLogin(
        InstitutionalLoginRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.LoginInstitutionallyAsync(
            request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("staff/login")]
    public async Task<ActionResult<AuthResponse>> StaffLogin(StaffLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginStaffAsync(request, RequestMetadata, cancellationToken);
        return FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("two-factor/verify")]
    public async Task<ActionResult<AuthResponse>> VerifyTwoFactor(TwoFactorRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.VerifyTwoFactorAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("two-factor/setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> SetupTwoFactor(TwoFactorSetupRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.BeginTwoFactorSetupAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("two-factor/enable")]
    public async Task<ActionResult<AuthResponse>> EnableTwoFactor(TwoFactorEnableRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.EnableTwoFactorAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] VerifyEmailRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.VerifyEmailAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        await authenticationService.RequestEmailVerificationAsync(request.Email, cancellationToken);
        return Accepted(new { message = "If the account is eligible, a verification email will be sent." });
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        await authenticationService.RequestPasswordResetAsync(request, RequestMetadata, cancellationToken);
        return Accepted(new { message = "If the account is eligible, password reset instructions will be sent." });
    }

    [AllowAnonymous]
    [HttpPost("student/recovery/lookup")]
    public async Task<ActionResult<StudentRecoveryLookupResponse>> BeginStudentRecovery(StudentRecoveryLookupRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.BeginStudentRecoveryAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("student/recovery/send")]
    public async Task<IActionResult> SendStudentRecoveryCode(StudentRecoverySendRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.SendStudentRecoveryCodeAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("student/recovery/verify")]
    public async Task<ActionResult<StudentRecoveryVerifyResponse>> VerifyStudentRecoveryCode(StudentRecoveryVerifyRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.VerifyStudentRecoveryCodeAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("student/recovery/complete")]
    public async Task<IActionResult> CompleteStudentRecovery(StudentRecoveryCompleteRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.CompleteStudentRecoveryAsync(request, RequestMetadata, cancellationToken));

    [AllowAnonymous]
    [HttpPost("admin/forgot-password")]
    public async Task<IActionResult> AdminForgotPassword(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        await authenticationService.RequestAdminPasswordResetAsync(request, RequestMetadata, cancellationToken);
        return Accepted(new { message = "If the staff account is eligible, password reset instructions will be sent." });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.ResetPasswordAsync(request, RequestMetadata, cancellationToken));

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        var user = await authenticationService.GetCurrentUserAsync(CurrentUserId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await authenticationService.ChangePasswordAsync(CurrentUserId, request, RequestMetadata, cancellationToken));

    [Authorize]
    [HttpPut("profile")]
    public async Task<ActionResult<CurrentUserDto>> UpdateProfile(UpdateProfileRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await authenticationService.UpdateProfileAsync(CurrentUserId, request, cancellationToken));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        await authenticationService.LogoutAsync(currentUser.UserId, currentUser.SessionId, RequestMetadata, cancellationToken);
        return NoContent();
    }
}
