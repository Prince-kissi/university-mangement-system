using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using FluentValidation;

namespace ComplaintSystem.Application.Validation;

public sealed class RegisterStudentRequestValidator : AbstractValidator<RegisterStudentRequest>
{
    public RegisterStudentRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Use an approved UMaT student email address.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a symbol.");
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ReferenceNumber).Must(UniversityEmailPolicy.IsValidReferenceNumber)
            .WithMessage("Invalid reference number.");
        RuleFor(x => x.IndexNumber).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9/-]+$");
        RuleFor(x => x.TelephoneNumber).NotEmpty().Matches("^\\+?[0-9][0-9 ()-]{7,19}$");
        RuleFor(x => x.Department).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Level).NotEmpty().Matches("^[1-4]00$");
        RuleFor(x => x.Gender).NotEmpty().Must(x => x is "Male" or "Female");
        RuleFor(x => x.AcceptTerms).Equal(true).WithMessage("Accept the terms and privacy policy to register.");
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow)).When(x => x.DateOfBirth.HasValue);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(254)
            .Must(value => UniversityEmailPolicy.IsValidReferenceNumber(value) ||
                UniversityEmailPolicy.IsApprovedStudentEmail(value) || UniversityEmailPolicy.IsApprovedStaffEmail(value))
            .WithMessage("Invalid student email, reference number, or password.");
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(128);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword);
    }
}

public sealed class PasswordResetRequestValidator : AbstractValidator<PasswordResetRequest>
{
    public PasswordResetRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(128);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword);
    }
}

public sealed class TwoFactorRequestValidator : AbstractValidator<TwoFactorRequest>
{
    public TwoFactorRequestValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Code).NotEmpty().Length(6, 8);
    }
}

public sealed class TwoFactorSetupRequestValidator : AbstractValidator<TwoFactorSetupRequest>
{
    public TwoFactorSetupRequestValidator() => RuleFor(x => x.ChallengeToken).NotEmpty().MaximumLength(128);
}

public sealed class TwoFactorEnableRequestValidator : AbstractValidator<TwoFactorEnableRequest>
{
    public TwoFactorEnableRequestValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Code).NotEmpty().Length(6, 8);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
    }
}

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress()
            .Must(UniversityEmailPolicy.IsApprovedStaffEmail)
            .WithMessage("Staff email must end with @umat.edu.gh.");
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.StaffId).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches("^\\+?[0-9][0-9 ()-]{7,19}$");
        RuleFor(x => x.Role).Must(x => x is AppRoles.DepartmentOfficer or AppRoles.Admin);
        RuleFor(x => x.DepartmentId).NotNull().When(x => x.Role == AppRoles.DepartmentOfficer);
        RuleFor(x => x.TemporaryPassword).NotEmpty().MinimumLength(12).MaximumLength(128)
            .Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]").Matches("[^a-zA-Z0-9]");
    }
}

public sealed class UpdateAccountRequestValidator : AbstractValidator<UpdateAccountRequest>
{
    public UpdateAccountRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.PhoneNumber).MaximumLength(24)
            .Matches("^\\+?[0-9][0-9 ()-]{7,19}$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.Role).Must(x => AppRoles.All.Contains(x, StringComparer.Ordinal));
        RuleFor(x => x.DepartmentId).NotNull().When(x => x.Role == AppRoles.DepartmentOfficer);
        RuleFor(x => x.StaffId).MaximumLength(32).Matches("^[A-Za-z0-9-]+$")
            .When(x => !string.IsNullOrWhiteSpace(x.StaffId));
        RuleFor(x => x.StudentReferenceNumber).Must(UniversityEmailPolicy.IsValidReferenceNumber)
            .When(x => !string.IsNullOrWhiteSpace(x.StudentReferenceNumber));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1_000);
    }
}
