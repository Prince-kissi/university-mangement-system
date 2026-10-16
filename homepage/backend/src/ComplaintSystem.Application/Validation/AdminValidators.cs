using ComplaintSystem.Application.Contracts;
using FluentValidation;

namespace ComplaintSystem.Application.Validation;

public sealed class DepartmentRequestValidator : AbstractValidator<DepartmentRequest>
{
    public DepartmentRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}

public sealed class CategoryRequestValidator : AbstractValidator<CategoryRequest>
{
    public CategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1_000);
    }
}

public sealed class RestoreBackupRequestValidator : AbstractValidator<RestoreBackupRequest>
{
    public RestoreBackupRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.TwoFactorCode).NotEmpty().Length(6, 8);
        RuleFor(x => x.WrittenReason).NotEmpty().MinimumLength(20).MaximumLength(1_000);
    }
}

public sealed class AnnouncementRequestValidator : AbstractValidator<AnnouncementRequest>
{
    public AnnouncementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(180);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4_000);
        RuleFor(x => x.Category).Must(x => new[] { "General Notice", "Complaint Update", "Academic Notice", "Administrative Notice", "Security Alert", "System Maintenance", "Emergency Notice", "Event or Programme", "Deadline Reminder" }.Contains(x));
        RuleFor(x => x.Priority).Must(x => new[] { "Low", "Normal", "High", "Urgent" }.Contains(x));
        RuleFor(x => x).Must(x => x.Everyone || (x.RecipientRoles?.Count > 0) || (x.UserIds?.Count > 0)).WithMessage("Select at least one recipient group or individual user.");
        RuleFor(x => x.DeliveryChannels).Must(x => x is { Count: > 0 }).WithMessage("Select at least one delivery channel.");
        RuleFor(x => x.ExpiresAt).GreaterThan(x => x.PublishAt ?? DateTimeOffset.UtcNow).When(x => x.ExpiresAt.HasValue);
    }
}

public sealed class UpdateSettingRequestValidator : AbstractValidator<UpdateSettingRequest>
{
    public UpdateSettingRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty().MaximumLength(4_000);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(1_000);
    }
}
