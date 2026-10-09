using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Infrastructure.Services;

/// <summary>Current time in UTC and in the business' time zone (Admin → Settings → Business; changes apply immediately).</summary>
internal sealed class DateTimeProvider(TimeProvider timeProvider, IOptionsMonitor<BusinessOptions> business, ILogger<DateTimeProvider> logger) : IDateTimeProvider
{
    // A reference (not a struct) so that concurrent requests always read a complete id/zone pair.
    private volatile CachedZone? _cached;

    public TimeZoneInfo BusinessTimeZone
    {
        get
        {
            var id = business.CurrentValue.TimeZoneId;
            var cached = _cached;
            if (cached is not null && cached.Id == id)
            {
                return cached.Zone;
            }

            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                logger.LogWarning("Time zone '{TimeZone}' not found; falling back to UTC", id);
                zone = TimeZoneInfo.Utc;
            }

            _cached = new CachedZone(id, zone);
            return zone;
        }
    }

    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public DateTime BusinessNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, BusinessTimeZone);

    public DateTime ToBusinessTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BusinessTimeZone);

    public DateTime ToUtc(DateTime businessLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(businessLocal, DateTimeKind.Unspecified), BusinessTimeZone);

    private sealed record CachedZone(string Id, TimeZoneInfo Zone);
}
