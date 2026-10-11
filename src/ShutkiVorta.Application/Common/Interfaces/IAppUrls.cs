namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>Builds absolute links to website pages (for emails, sitemaps and structured data).</summary>
public interface IAppUrls
{
    string BaseUrl { get; }

    /// <summary>
    /// The website address (Site:BaseUrl) is set. When it is not, links are built from the current request's address, which a
    /// visitor controls: fine for the admin's own screen, never for a secret link sent by email.
    /// </summary>
    bool HasConfiguredBaseUrl { get; }

    string Absolute(string relativePath);
    string Home();
    string Menu();
    string MenuItem(string slug);
    string OrderStatus(string orderNumber, string trackingToken);
    string CustomerOrder(string orderNumber);
    string AdminOrder(string orderNumber);
    string AdminInquiries();
    string AdminStandingOrder(int id);
    string CustomerStandingOrder(string reference);
    string Restaurants();
    string ConfirmEmail(string userId, string code);
    string ResetPassword(string email, string code);

    /// <summary>The set-password page for an invited staff member (the reset-password page in invitation mode).</summary>
    string StaffInvitation(string email, string code);

    string Login();
}
