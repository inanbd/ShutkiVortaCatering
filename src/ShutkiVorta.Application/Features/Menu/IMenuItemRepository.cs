using ShutkiVorta.Domain.Menu;

namespace ShutkiVorta.Application.Features.Menu;

public interface IMenuItemRepository
{
    Task<IReadOnlyList<MenuItem>> GetAllAsync(bool includeUnavailable, CancellationToken cancellationToken = default);
    Task<MenuItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<MenuItem?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuItem>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string slug, int? excludeId, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MenuItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(MenuItem item, CancellationToken cancellationToken = default);
    Task<bool> IsReferencedByOrdersAsync(int id, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
