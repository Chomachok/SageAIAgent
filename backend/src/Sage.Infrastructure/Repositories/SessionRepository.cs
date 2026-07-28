using Microsoft.EntityFrameworkCore;
using Sage.Core.Abstractions;
using Sage.Core.Entities;
using Sage.Infrastructure.Data;

namespace Sage.Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly AppDbContext _context;

    public SessionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task AddMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        _context.Messages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<Session>> ListAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        return await _context.Sessions
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
