using Dapper;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Domain.Inquiries;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

internal sealed class CateringInquiryRepository(IDbConnectionFactory connections, ISqlDialect dialect) : ICateringInquiryRepository
{
    private const string Columns = "Id, Topic, Name, Email, Phone, EventDate, GuestCount, Message, IsHandled, CreatedAtUtc, HandledAtUtc";

    public async Task AddAsync(CateringInquiry inquiry, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO CateringInquiries (Topic, Name, Email, Phone, EventDate, GuestCount, Message, IsHandled, CreatedAtUtc, HandledAtUtc)
            VALUES (@Topic, @Name, @Email, @Phone, @EventDate, @GuestCount, @Message, @IsHandled, @CreatedAtUtc, @HandledAtUtc)
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(dialect.InsertReturningId(sql), inquiry, cancellationToken: cancellationToken));
        inquiry.AssignId(id);
    }

    public async Task<CateringInquiry?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CateringInquiry>(new CommandDefinition(
            $"SELECT {Columns} FROM CateringInquiries WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(CateringInquiry inquiry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE CateringInquiries SET IsHandled = @IsHandled, HandledAtUtc = @HandledAtUtc WHERE Id = @Id",
            new { inquiry.Id, inquiry.IsHandled, inquiry.HandledAtUtc },
            cancellationToken: cancellationToken));
    }

    public async Task<PagedResult<CateringInquiry>> ListAsync(bool onlyOpen, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var where = onlyOpen ? "WHERE IsHandled = 0" : string.Empty;
        await using var connection = await connections.OpenAsync(cancellationToken);

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM CateringInquiries {where}", cancellationToken: cancellationToken));
        if (total == 0)
        {
            return PagedResult<CateringInquiry>.Empty(page, pageSize);
        }

        var items = await connection.QueryAsync<CateringInquiry>(new CommandDefinition(
            dialect.Page($"SELECT {Columns} FROM CateringInquiries {where} ORDER BY IsHandled, CreatedAtUtc DESC"),
            new { Skip = (page - 1) * pageSize, Take = pageSize },
            cancellationToken: cancellationToken));

        return new PagedResult<CateringInquiry>(items.AsList(), total, page, pageSize);
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM CateringInquiries WHERE IsHandled = 0", cancellationToken: cancellationToken));
    }
}
