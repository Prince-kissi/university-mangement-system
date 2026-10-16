namespace ComplaintSystem.Application.Contracts;

public sealed record SetupStatusDto(bool RequiresSetup);
public sealed record CreateFirstSuperAdminRequest(
    string FirstName, string LastName, string? AdministratorId, string Email,
    string PhoneNumber, string Password, string ConfirmPassword);
public sealed record FirstSuperAdminCreatedDto(string AdministratorId);
