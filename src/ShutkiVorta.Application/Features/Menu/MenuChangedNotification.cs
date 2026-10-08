using MediatR;

namespace ShutkiVorta.Application.Features.Menu;

/// <summary>Published whenever the menu changes so caches (sitemap, robots.txt) can be refreshed.</summary>
public sealed record MenuChangedNotification(int MenuItemId) : INotification;
