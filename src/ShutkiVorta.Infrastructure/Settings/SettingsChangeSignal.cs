using Microsoft.Extensions.Primitives;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Settings;

internal sealed class SettingsChangeSignal(SettingsConfiguration settings) : ISettingsChangeSignal
{
    public IChangeToken GetChangeToken() => settings.Root.GetReloadToken();
}
