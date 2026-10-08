using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Infrastructure.Services;

internal sealed class DateTimeProvider : IDateTimeProvider
{
    private readonly TimeProvider _timeProvider;

    public DateTimeProvider(TimeProvider timeProvider, IOptions<BusinessOptions> business, ILogger<DateTimeProvider> logger)
    {
        _timeProvider = timeProvider;
        try
        {
            BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById(business.Value.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Time zone '{TimeZone}' not found; falling back to UTC", business.Value.TimeZoneId);
            BusinessTimeZone = TimeZoneInfo.Utc;
        }
    }

    public TimeZoneInfo BusinessTimeZone { get; }

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public DateTime BusinessNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, BusinessTimeZone);

    public DateTime ToBusinessTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BusinessTimeZone);

    public DateTime ToUtc(DateTime businessLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(businessLocal, DateTimeKind.Unspecified), BusinessTimeZone);
}
