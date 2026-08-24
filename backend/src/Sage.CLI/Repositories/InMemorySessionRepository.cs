using Sage.Core.Abstractions;
using Sage.Core.Entities;

namespace Sage.CLI.Repositories;

public class InMemorySessionRepository : ISessionRepository
{
    private readonly List<Session> _sessions = new();
    private readonly object _lock = new();

    public Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == id);
            return Task.FromResult(session);
        }
    }

    public Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            session.CreatedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            _sessions.Add(session);
            return Task.FromResult(session);
        }
    }

    public Task AddMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == message.SessionId);
            if (session != null)
            {
                session.Messages.Add(message);
                session.UpdatedAt = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }
    }

    public Task<IEnumerable<Session>> ListAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var result = _sessions.OrderByDescending(s => s.UpdatedAt).Take(limit).ToList();
            return Task.FromResult(result.AsEnumerable());
        }
    }

    public Task UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var existing = _sessions.FirstOrDefault(s => s.Id == session.Id);
            if (existing != null)
            {
                existing.Title = session.Title;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == id);
            if (session != null)
            {
                _sessions.Remove(session);
            }
            return Task.CompletedTask;
        }
    }
}