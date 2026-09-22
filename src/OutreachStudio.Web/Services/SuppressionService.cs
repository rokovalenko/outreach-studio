using Microsoft.EntityFrameworkCore;
using OutreachStudio.Data;

namespace OutreachStudio.Web.Services;

/// <summary>
/// The do not contact list. It is read at send time rather than at estimate time, so adding an
/// address here does not change any audience count and does not invalidate the audience cache.
/// </summary>
public sealed class SuppressionService(OutreachDbContext db)
{
    public async Task<IReadOnlyList<Suppression>> ListAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return await db.Suppressions.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct) => await db.Suppressions.CountAsync(ct);

    public async Task AddAsync(string email, string reason, CancellationToken ct)
    {
        var address = email.Trim().ToLowerInvariant();
        if (await db.Suppressions.AnyAsync(s => s.Email == address, ct))
        {
            return;
        }
        db.Suppressions.Add(new Suppression { Email = address, Reason = reason, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(string email, CancellationToken ct) =>
        await db.Suppressions.Where(s => s.Email == email).ExecuteDeleteAsync(ct);
}
