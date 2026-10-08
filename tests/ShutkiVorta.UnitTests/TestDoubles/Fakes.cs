using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.UnitTests.TestDoubles;

/// <summary>Deterministic clock. Business time zone is UTC to keep arithmetic obvious.</summary>
internal sealed class FakeClock(DateTime utcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;
    public DateTime BusinessNow => UtcNow;
    public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    public DateTime ToBusinessTime(DateTime utc) => utc;
    public DateTime ToUtc(DateTime businessLocal) => businessLocal;
}

internal sealed class FakeUrls : IAppUrls
{
    public string BaseUrl => "https://shutki.test";
    public string Absolute(string relativePath) => BaseUrl + "/" + relativePath.TrimStart('/');
    public string Home() => BaseUrl + "/";
    public string Menu() => Absolute("/menu");
    public string MenuItem(string slug) => Absolute($"/menu/{slug}");
    public string OrderStatus(string orderNumber, string trackingToken) => Absolute($"/order/{orderNumber}?token={trackingToken}");
    public string CustomerOrder(string orderNumber) => Absolute($"/account/orders/{orderNumber}");
    public string AdminOrder(string orderNumber) => Absolute($"/admin/orders/{orderNumber}");
    public string AdminInquiries() => Absolute("/admin/inquiries");
    public string ConfirmEmail(string userId, string code) => Absolute($"/account/confirm-email?userId={userId}&code={code}");
    public string ResetPassword(string email, string code) => Absolute($"/account/reset-password?email={email}&code={code}");
    public string Login() => Absolute("/account/login");
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public bool IsAuthenticated => UserId is not null;
    public bool IsAdmin { get; set; }
}
