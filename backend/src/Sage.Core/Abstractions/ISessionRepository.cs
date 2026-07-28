using Sage.Core.Entities;

namespace Sage.Core.Abstractions;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default);
    Task AddMessageAsync(Message message, CancellationToken cancellationToken = default);
    Task<IEnumerable<Session>> ListAsync(int limit = 20, CancellationToken cancellationToken = default);
}
