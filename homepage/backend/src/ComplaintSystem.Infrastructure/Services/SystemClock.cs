using ComplaintSystem.Application.Abstractions;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
