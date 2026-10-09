using Microsoft.Extensions.Primitives;

namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>Fires whenever the database-backed settings are reloaded (saved here or on another server).</summary>
public interface ISettingsChangeSignal
{
    IChangeToken GetChangeToken();
}
