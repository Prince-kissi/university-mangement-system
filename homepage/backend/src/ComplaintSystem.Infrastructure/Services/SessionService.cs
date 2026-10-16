using System.Security.Cryptography;
using System.Text;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class SessionService(ApplicationDbContext dbContext, IDistributedCache cache, IClock clock) : ISessionService
{
    public async Task<string> CreateAsync(Guid userId, RequestMetadata metadata, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Hash(rawToken);
        var expiresAt = clock.UtcNow.Add(lifetime);
        dbContext.ActiveSessions.Add(new ActiveSession
        {
            UserId = userId,
            SessionTokenHash = tokenHash,
            IpAddress = metadata.IpAddress,
            UserAgent = metadata.UserAgent,
            ExpiresAt = expiresAt,
            LastSeenAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.SetStringAsync(CacheKey(tokenHash), "active", new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = expiresAt
        }, cancellationToken);
        return rawToken;
    }

    public async Task<bool> IsActiveAsync(string sessionId, CancellationToken cancellationToken)
    {
        var hash = Hash(sessionId);
        if (await cache.GetStringAsync(CacheKey(hash), cancellationToken) == "revoked")
        {
            return false;
        }

        var session = await dbContext.ActiveSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SessionTokenHash == hash, cancellationToken);
        var active = session is not null && session.RevokedAt is null && session.ExpiresAt > clock.UtcNow;
        if (active)
        {
            await cache.SetStringAsync(CacheKey(hash), "active", new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session!.ExpiresAt
            }, cancellationToken);
        }
        return active;
    }

    public async Task TouchAsync(string sessionId, CancellationToken cancellationToken)
    {
        var hash = Hash(sessionId);
        var session = await dbContext.ActiveSessions.SingleOrDefaultAsync(x => x.SessionTokenHash == hash, cancellationToken);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= clock.UtcNow)
        {
            return;
        }

        if (clock.UtcNow - session.LastSeenAt > TimeSpan.FromMinutes(2))
        {
            session.LastSeenAt = clock.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokeAsync(string sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken)
    {
        var hash = Hash(sessionId);
        var session = await dbContext.ActiveSessions.SingleOrDefaultAsync(x => x.SessionTokenHash == hash, cancellationToken);
        if (session is null || session.RevokedAt is not null)
        {
            return;
        }
        session.RevokedAt = clock.UtcNow;
        session.RevokedByUserId = revokedBy;
        session.RevocationReason = reason;
        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.SetStringAsync(CacheKey(hash), "revoked", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        }, cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.ActiveSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > clock.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = clock.UtcNow;
            session.RevokedByUserId = revokedBy;
            session.RevocationReason = reason;
            await cache.SetStringAsync(CacheKey(session.SessionTokenHash), "revoked", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            }, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string CacheKey(string hash) => $"session:{hash}";
}
