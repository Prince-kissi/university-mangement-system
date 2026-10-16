using System.Security.Claims;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Security;

namespace ComplaintSystem.Api.Services;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var value) ? value : null;
    public Guid? DepartmentId => Guid.TryParse(Principal?.FindFirstValue(ClaimNames.DepartmentId), out var value) ? value : null;
    public string? SessionId => Principal?.FindFirstValue(ClaimNames.SessionId);
    public string? ActualRole => Principal?.FindFirstValue(ClaimNames.ActualRole);
    public string? ActiveRole => Principal?.FindFirstValue(ClaimNames.ActiveRole);
    public bool IsInRole(string role) => string.Equals(ActiveRole, role, StringComparison.Ordinal) ||
        (ActiveRole is null && Principal?.IsInRole(role) == true);
}
