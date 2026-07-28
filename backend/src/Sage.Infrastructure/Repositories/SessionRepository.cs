using Microsoft.EntityFrameworkCore;
using Sage.Core.Abstractions;
using Sage.Core.Entities;
using Sage.Infrastructure.Data;

namespace Sage.Infrastructure.Repositories;

public class SessionRepository(AppDbContext context) : ISessionRepository
{
    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        context.Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task AddMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        context.Messages.Add(message);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<Session>> ListAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        return await context.Sessions
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
    
    public async Task UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        context.Sessions.Update(session);
        await context.SaveChangesAsync(cancellationToken);
    }
}
