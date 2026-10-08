namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>Builds absolute links to website pages (for emails, sitemaps and structured data).</summary>
public interface IAppUrls
{
    string BaseUrl { get; }
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
    string Login();
}
