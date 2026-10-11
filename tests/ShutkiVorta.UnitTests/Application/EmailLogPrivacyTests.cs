using NSubstitute;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

/// <summary>An order-status link lets its holder cancel the order, so the email log shows it only to staff who may change orders.</summary>
public sealed class EmailLogPrivacyTests
{
    private const string Link = "https://shutki.test/order/SV-261008-AB12?token=s3cr3t-T0ken_x";

    private static EmailLogHandlers Handler(FakeCurrentStaff staff)
    {
        var log = Substitute.For<IEmailLog>();
        log.GetAsync(7, Arg.Any<CancellationToken>()).Returns(_ => new EmailLogEntryDto
        {
            Id = 7,
            Subject = "Order SV-261008-AB12 confirmed",
            HtmlBody = $"<p>Track it here: <a href=\"{Link}\">your order</a></p>",
            TextBody = $"Track it here: {Link}",
        });
        return new EmailLogHandlers(log, Substitute.For<IEmailTransport>(), staff);
    }

    [Fact]
    public async Task WithoutOrdersManage_TheTokenIsHidden()
    {
        var entry = (await Handler(FakeCurrentStaff.With(StaffRole.Manager, Permissions.EmailsView, Permissions.OrdersView))
            .Handle(new GetEmailLogEntryQuery(7), CancellationToken.None))!;

        Assert.True(entry.PrivateLinksHidden);
        Assert.DoesNotContain("s3cr3t", entry.HtmlBody);
        Assert.DoesNotContain("s3cr3t", entry.TextBody);
        Assert.Contains("/order/SV-261008-AB12?token=hidden", entry.HtmlBody);
    }

    [Fact]
    public async Task WithOrdersManage_TheEmailIsShownAsSent()
    {
        var entry = (await Handler(FakeCurrentStaff.With(StaffRole.Manager, Permissions.EmailsView, Permissions.OrdersManage))
            .Handle(new GetEmailLogEntryQuery(7), CancellationToken.None))!;

        Assert.False(entry.PrivateLinksHidden);
        Assert.Contains(Link, entry.HtmlBody);
    }
}
