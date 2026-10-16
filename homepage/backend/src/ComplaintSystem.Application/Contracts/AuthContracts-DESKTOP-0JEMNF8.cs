namespace ComplaintSystem.Application.Contracts;

public sealed record RegisterStudentRequest(
    string? Email, string Password, string ConfirmPassword, string FirstName, string LastName,
    string ReferenceNumber, string TelephoneNumber = "", string Department = "",
    string Level = "", DateOnly? DateOfBirth = null, string? IndexNumber = null,
    string? Programme = null, string Gender = "", bool AcceptTerms = false, string? MiddleName = null);
public sealed record StudentRegistrationPendingResponse(Guid PendingRegistrationId, string MaskedPhone, string MaskedEmail);
public sealed record StudentVerificationSendRequest(Guid PendingRegistrationId, string Method);
public sealed record StudentVerificationCodeRequest(Guid PendingRegistrationId, string Code);
public sealed record StudentVerificationSendResponse(string Method, string MaskedDestination, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);
public sealed record LoginRequest(string Identifier, string Password, bool RememberMe = false);
public sealed record InstitutionalLoginRequest(string Credential);
public sealed record InstitutionalLoginConfigurationDto(
    bool Enabled, string Provider, string ClientId, string HostedDomain);
public sealed record StaffLoginRequest(string Email, string Password, bool RememberMe = false,
    string LoginPage = "staff", Guid? ActiveDepartmentId = null, string? Identifier = null);
public sealed record VerifyEmailRequest(Guid UserId, string Token);
public sealed record PasswordResetRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword, string ConfirmPassword);
public sealed record StudentRecoveryLookupRequest(string Identifier);
public sealed record StudentRecoveryMethodDto(string Method, string MaskedDestination);
public sealed record StudentRecoveryLookupResponse(Guid RecoveryId, IReadOnlyCollection<StudentRecoveryMethodDto> Methods);
public sealed record StudentRecoverySendRequest(Guid RecoveryId, string Method);
public sealed record StudentRecoveryVerifyRequest(Guid RecoveryId, string Code);
public sealed record StudentRecoveryVerifyResponse(string VerificationToken);
public sealed record StudentRecoveryCompleteRequest(Guid RecoveryId, string VerificationToken, string NewPassword, string ConfirmPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
public sealed record UpdateProfileRequest(
    string FirstName, string LastName, string? Title = null, string? MiddleName = null,
    string? Gender = null, DateOnly? DateOfBirth = null, Guid? DepartmentId = null, string? StaffId = null,
    string? NationalId = null, string? Position = null, string? OfficeLocation = null,
    string? RecoveryEmail = null, string? PhoneNumber = null, string? AlternativePhoneNumber = null,
    string? FacultyOrSchool = null, int? AdmissionYear = null, string? PersonalEmail = null,
    string? ResidentialAddress = null);
public sealed record TwoFactorRequest(string ChallengeToken, string Code, bool RememberMachine = false);
public sealed record TwoFactorSetupRequest(string ChallengeToken);
public sealed record TwoFactorEnableRequest(string ChallengeToken, string Code);
public sealed record AuthResponse(
    bool Authenticated, bool RequiresEmailVerification, bool RequiresTwoFactor,
    bool RequiresTwoFactorSetup, string? ChallengeToken, CurrentUserDto? User,
    bool ShowWelcomeMessage = false);
public sealed record CurrentUserDto(
    Guid Id, string Email, string FirstName, string LastName, IReadOnlyCollection<string> Roles,
    Guid? DepartmentId, string? ReferenceNumber, bool ForcePasswordReset,
    string? TelephoneNumber, string? Department,
    string? Level, DateOnly? DateOfBirth, DateTimeOffset AccountCreationDate,
    string? ActualRole = null, string? ActiveRole = null, Guid? ActiveDepartmentId = null,
    Guid? StudentId = null, string? IndexNumber = null, string? Programme = null, string? Gender = null,
    string? Title = null, string? MiddleName = null, string? StaffId = null,
    string? NationalId = null, string? Position = null, string? OfficeLocation = null,
    string? RecoveryEmail = null, string? AlternativePhoneNumber = null, string? ProfileImageUrl = null,
    string? FacultyOrSchool = null, int? AdmissionYear = null, string? PersonalEmail = null,
    string? ResidentialAddress = null);
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);
public sealed record CsrfTokenResponse(string Token);
