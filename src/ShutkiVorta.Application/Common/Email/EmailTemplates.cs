namespace ShutkiVorta.Application.Common.Email;

/// <summary>Names of the HTML templates embedded in the infrastructure layer.</summary>
public static class EmailTemplates
{
    public const string AdminNewOrder = "AdminNewOrder";
    public const string AdminOrderCancelled = "AdminOrderCancelled";
    public const string AdminNewInquiry = "AdminNewInquiry";
    public const string OrderConfirmation = "OrderConfirmation";
    public const string OrderStatusUpdate = "OrderStatusUpdate";
    public const string InquiryReceived = "InquiryReceived";
    public const string ConfirmEmail = "ConfirmEmail";
    public const string Welcome = "Welcome";
    public const string ResetPassword = "ResetPassword";
    public const string TestEmail = "TestEmail";
}
