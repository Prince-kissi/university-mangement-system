using ComplaintSystem.Application.Contracts;
using FluentValidation;

namespace ComplaintSystem.Application.Validation;

public sealed class SubmitComplaintRequestValidator : AbstractValidator<SubmitComplaintRequest>
{
    public SubmitComplaintRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(5).MaximumLength(180);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(20).MaximumLength(10_000);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.DepartmentId).NotEmpty();
    }
}

public sealed class SubmitAnonymousComplaintRequestValidator : AbstractValidator<SubmitAnonymousComplaintRequest>
{
    public SubmitAnonymousComplaintRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(5).MaximumLength(180);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(20).MaximumLength(10_000);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.DepartmentId).NotEmpty();
    }
}

public sealed class AddResponseRequestValidator : AbstractValidator<AddResponseRequest>
{
    public AddResponseRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
}

public sealed class AnonymousTrackRequestValidator : AbstractValidator<AnonymousTrackRequest>
{
    public AnonymousTrackRequestValidator()
    {
        RuleFor(x => x.ComplaintId).NotEmpty().MaximumLength(32);
        RuleFor(x => x.TrackingCode).NotEmpty().MaximumLength(128);
    }
}

public sealed class AnonymousResponseRequestValidator : AbstractValidator<AnonymousResponseRequest>
{
    public AnonymousResponseRequestValidator()
    {
        RuleFor(x => x.ComplaintId).NotEmpty().MaximumLength(32);
        RuleFor(x => x.TrackingCode).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
    }
}

public sealed class AssignComplaintRequestValidator : AbstractValidator<AssignComplaintRequest>
{
    public AssignComplaintRequestValidator()
    {
        RuleFor(x => x.OfficerUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1_000);
    }
}

public sealed class ReassignComplaintRequestValidator : AbstractValidator<ReassignComplaintRequest>
{
    public ReassignComplaintRequestValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1_000);
    }
}

public sealed class ChangeComplaintStatusRequestValidator : AbstractValidator<ChangeComplaintStatusRequest>
{
    public ChangeComplaintStatusRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1_000);
}

public sealed class AddInternalNoteRequestValidator : AbstractValidator<AddInternalNoteRequest>
{
    public AddInternalNoteRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
}

public sealed class ChangeComplaintPriorityRequestValidator : AbstractValidator<ChangeComplaintPriorityRequest>
{
    public ChangeComplaintPriorityRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1_000);
}
