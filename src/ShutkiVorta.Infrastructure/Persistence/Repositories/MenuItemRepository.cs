using Dapper;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Menu;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

internal sealed class MenuItemRepository(IDbConnectionFactory connections, ISqlDialect dialect) : IMenuItemRepository
{
    private const string Columns = """
        Id, Name, BengaliName, Slug, Category, ShortDescription, Description, Ingredients, PricePerUnit, Unit,
        MinimumQuantity, QuantityStep, SpiceLevel, ImageUrl, ImageAlt, ImageCredit, IsAvailable, IsFeatured,
        SortOrder, MetaTitle, MetaDescription, CreatedAtUtc, UpdatedAtUtc
        """;

    public async Task<IReadOnlyList<MenuItem>> GetAllAsync(bool includeUnavailable, CancellationToken cancellationToken = default)
    {
        var where = includeUnavailable ? string.Empty : "WHERE IsAvailable = 1";
        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = await connection.QueryAsync<MenuItem>(new CommandDefinition(
            $"SELECT {Columns} FROM MenuItems {where} ORDER BY Category, SortOrder, Name", cancellationToken: cancellationToken));
        return items.AsList();
    }

    public async Task<MenuItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<MenuItem>(new CommandDefinition(
            $"SELECT {Columns} FROM MenuItems WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<MenuItem?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<MenuItem>(new CommandDefinition(
            $"SELECT {Columns} FROM MenuItems WHERE Slug = @Slug", new { Slug = slug }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<MenuItem>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = await connection.QueryAsync<MenuItem>(new CommandDefinition(
            $"SELECT {Columns} FROM MenuItems WHERE Id IN @Ids", new { Ids = ids.Distinct().ToArray() }, cancellationToken: cancellationToken));
        return items.AsList();
    }

    public async Task<bool> SlugExistsAsync(string slug, int? excludeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM MenuItems WHERE Slug = @Slug AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
            new { Slug = slug, ExcludeId = excludeId },
            cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM MenuItems", cancellationToken: cancellationToken));
    }

    public async Task AddAsync(MenuItem item, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO MenuItems (Name, BengaliName, Slug, Category, ShortDescription, Description, Ingredients, PricePerUnit, Unit,
                MinimumQuantity, QuantityStep, SpiceLevel, ImageUrl, ImageAlt, ImageCredit, IsAvailable, IsFeatured,
                SortOrder, MetaTitle, MetaDescription, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@Name, @BengaliName, @Slug, @Category, @ShortDescription, @Description, @Ingredients, @PricePerUnit, @Unit,
                @MinimumQuantity, @QuantityStep, @SpiceLevel, @ImageUrl, @ImageAlt, @ImageCredit, @IsAvailable, @IsFeatured,
                @SortOrder, @MetaTitle, @MetaDescription, @CreatedAtUtc, @UpdatedAtUtc)
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(dialect.InsertReturningId(sql), item, cancellationToken: cancellationToken));
        item.AssignId(id);
    }

    public async Task UpdateAsync(MenuItem item, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE MenuItems SET
                Name = @Name, BengaliName = @BengaliName, Slug = @Slug, Category = @Category,
                ShortDescription = @ShortDescription, Description = @Description, Ingredients = @Ingredients,
                PricePerUnit = @PricePerUnit, Unit = @Unit, MinimumQuantity = @MinimumQuantity, QuantityStep = @QuantityStep,
                SpiceLevel = @SpiceLevel, ImageUrl = @ImageUrl, ImageAlt = @ImageAlt, ImageCredit = @ImageCredit,
                IsAvailable = @IsAvailable, IsFeatured = @IsFeatured, SortOrder = @SortOrder,
                MetaTitle = @MetaTitle, MetaDescription = @MetaDescription, UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, item, cancellationToken: cancellationToken));
    }

    public async Task<bool> IsReferencedByOrdersAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM OrderLines WHERE MenuItemId = @Id", new { Id = id }, cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM MenuItems WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }
}
