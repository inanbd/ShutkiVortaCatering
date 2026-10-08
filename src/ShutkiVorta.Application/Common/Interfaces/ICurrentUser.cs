namespace ShutkiVorta.Application.Common.Interfaces;

public interface ICurrentUser
{
    string? UserId { get; }
    string? Email { get; }
    string? Name { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
}
