using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Inquiries;

public enum InquiryTopic
{
    Catering = 1,
    General = 2,
    JoinKitchen = 3,
}

public static class InquiryTopicExtensions
{
    public static string DisplayName(this InquiryTopic topic) => topic switch
    {
        InquiryTopic.Catering => "Event catering",
        InquiryTopic.General => "General question",
        InquiryTopic.JoinKitchen => "Wants to cook with us",
        _ => topic.ToString(),
    };
}

/// <summary>A request for event catering (weddings, dawat, Eid, Pohela Boishakh...) or a general question.</summary>
public sealed class CateringInquiry : Entity
{
    private CateringInquiry()
    {
    }

    public InquiryTopic Topic { get; private set; } = InquiryTopic.Catering;
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public DateTime? EventDate { get; private set; }
    public int? GuestCount { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public bool IsHandled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? HandledAtUtc { get; private set; }

    public static CateringInquiry Submit(string name, string email, string? phone, DateTime? eventDate, int? guestCount, string message, DateTime nowUtc) =>
        Submit(InquiryTopic.Catering, name, email, phone, eventDate, guestCount, message, nowUtc);

    public static CateringInquiry Submit(InquiryTopic topic, string name, string email, string? phone, DateTime? eventDate, int? guestCount, string message, DateTime nowUtc)
    {
        if (guestCount is < 1 or > 5000)
        {
            throw new DomainException("Guest count must be between 1 and 5000.");
        }

        return new CateringInquiry
        {
            Topic = Enum.IsDefined(topic) ? topic : InquiryTopic.General,
            Name = Guard.NotEmpty(name, "Name", 120),
            Email = Guard.NotEmpty(email, "Email", 256).ToLowerInvariant(),
            Phone = Guard.Optional(phone, "Phone", 32),
            EventDate = eventDate?.Date,
            GuestCount = guestCount,
            Message = Guard.NotEmpty(message, "Message", 4000),
            CreatedAtUtc = nowUtc,
        };
    }

    public void MarkHandled(bool handled, DateTime nowUtc)
    {
        IsHandled = handled;
        HandledAtUtc = handled ? nowUtc : null;
    }
}
