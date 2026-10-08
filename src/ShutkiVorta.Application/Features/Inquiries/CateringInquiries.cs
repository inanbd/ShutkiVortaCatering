using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Inquiries;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Inquiries;

public interface ICateringInquiryRepository
{
    Task AddAsync(CateringInquiry inquiry, CancellationToken cancellationToken = default);
    Task<CateringInquiry?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task UpdateAsync(CateringInquiry inquiry, CancellationToken cancellationToken = default);
    Task<PagedResult<CateringInquiry>> ListAsync(bool onlyOpen, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountOpenAsync(CancellationToken cancellationToken = default);
}

public sealed record SubmitCateringInquiryCommand(
    string Name,
    string Email,
    string? Phone,
    DateOnly? EventDate,
    int? GuestCount,
    string Message,
    InquiryTopic Topic = InquiryTopic.Catering) : IRequest<int>;

public sealed record CateringInquirySubmittedNotification(int InquiryId) : INotification;

public sealed record GetInquiriesQuery(bool OnlyOpen, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<CateringInquiryDto>>, IRequireAdmin;

public sealed record SetInquiryHandledCommand(int Id, bool IsHandled) : IRequest, IRequireAdmin;

public sealed record CateringInquiryDto(
    int Id,
    InquiryTopic Topic,
    string Name,
    string Email,
    string? Phone,
    DateTime? EventDate,
    int? GuestCount,
    string Message,
    bool IsHandled,
    DateTime CreatedAtLocal)
{
    public string TopicName => Topic.DisplayName();
}

public sealed class SubmitCateringInquiryCommandValidator : AbstractValidator<SubmitCateringInquiryCommand>
{
    public SubmitCateringInquiryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Please tell us your name.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.GuestCount).InclusiveBetween(1, 5000).When(x => x.GuestCount.HasValue);
        RuleFor(x => x.Message).NotEmpty().WithMessage("Please tell us a little about your event or question.").MaximumLength(4000);
        RuleFor(x => x.Topic).IsInEnum();
    }
}

internal sealed class CateringInquiryHandlers(
    ICateringInquiryRepository repository,
    IDateTimeProvider clock,
    IPublisher publisher) :
    IRequestHandler<SubmitCateringInquiryCommand, int>,
    IRequestHandler<GetInquiriesQuery, PagedResult<CateringInquiryDto>>,
    IRequestHandler<SetInquiryHandledCommand>
{
    public async Task<int> Handle(SubmitCateringInquiryCommand request, CancellationToken cancellationToken)
    {
        if (request.EventDate is { } date && date < DateOnly.FromDateTime(clock.BusinessNow))
        {
            throw new ValidationException(nameof(SubmitCateringInquiryCommand.EventDate), "The event date cannot be in the past.");
        }

        CateringInquiry inquiry;
        try
        {
            inquiry = CateringInquiry.Submit(
                request.Topic, request.Name, request.Email, request.Phone, request.EventDate?.ToDateTime(TimeOnly.MinValue), request.GuestCount, request.Message, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await repository.AddAsync(inquiry, cancellationToken);
        await publisher.Publish(new CateringInquirySubmittedNotification(inquiry.Id), cancellationToken);
        return inquiry.Id;
    }

    public async Task<PagedResult<CateringInquiryDto>> Handle(GetInquiriesQuery request, CancellationToken cancellationToken)
    {
        var (page, size) = Paging.Normalize(request.Page, request.PageSize);
        var result = await repository.ListAsync(request.OnlyOpen, page, size, cancellationToken);
        var items = result.Items
            .Select(i => new CateringInquiryDto(i.Id, i.Topic, i.Name, i.Email, i.Phone, i.EventDate, i.GuestCount, i.Message, i.IsHandled, clock.ToBusinessTime(i.CreatedAtUtc)))
            .ToList();
        return new PagedResult<CateringInquiryDto>(items, result.TotalCount, result.Page, result.PageSize);
    }

    public async Task Handle(SetInquiryHandledCommand request, CancellationToken cancellationToken)
    {
        var inquiry = await repository.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Inquiry", request.Id);
        inquiry.MarkHandled(request.IsHandled, clock.UtcNow);
        await repository.UpdateAsync(inquiry, cancellationToken);
    }
}

internal sealed class CateringInquiryEmailHandler(
    ICateringInquiryRepository repository,
    IEmailTemplateRenderer renderer,
    IEmailService email,
    IAppUrls urls,
    IOptions<EmailOptions> emailOptions,
    ILogger<CateringInquiryEmailHandler> logger) : INotificationHandler<CateringInquirySubmittedNotification>
{
    public async Task Handle(CateringInquirySubmittedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var inquiry = await repository.GetByIdAsync(notification.InquiryId, cancellationToken);
            if (inquiry is null)
            {
                return;
            }

            var model = new Dictionary<string, object?>
            {
                ["CustomerName"] = inquiry.Name,
                ["CustomerFirstName"] = inquiry.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? inquiry.Name,
                ["CustomerEmail"] = inquiry.Email,
                ["CustomerPhone"] = inquiry.Phone,
                ["EventDate"] = inquiry.EventDate is { } d ? Format.Date(d) : null,
                ["GuestCount"] = inquiry.GuestCount?.ToString(Format.Culture),
                ["Message"] = inquiry.Message,
                ["AdminInquiriesUrl"] = urls.AdminInquiries(),
                ["MenuUrl"] = urls.Menu(),
                ["TopicName"] = inquiry.Topic.DisplayName(),
                ["IsJoinKitchen"] = inquiry.Topic == InquiryTopic.JoinKitchen,
                ["IsCatering"] = inquiry.Topic == InquiryTopic.Catering,
                ["IsGeneral"] = inquiry.Topic == InquiryTopic.General,
            };

            var (adminSubject, replySubject) = inquiry.Topic switch
            {
                InquiryTopic.JoinKitchen => ($"👩‍🍳 {inquiry.Name} would like to cook with us", "Thank you for wanting to join our kitchen"),
                InquiryTopic.General => ($"💬 New message from {inquiry.Name}", "We received your message"),
                _ => ($"💬 New catering inquiry from {inquiry.Name}", "We received your catering inquiry"),
            };

            var admins = emailOptions.Value.AdminRecipients.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (admins.Count > 0)
            {
                var adminEmail = renderer.Render(EmailTemplates.AdminNewInquiry, adminSubject, model);
                foreach (var admin in admins)
                {
                    await email.QueueAsync(EmailMessage.Create(admin, adminEmail, replyTo: inquiry.Email), cancellationToken);
                }
            }

            var reply = renderer.Render(EmailTemplates.InquiryReceived, replySubject, model);
            await email.QueueAsync(EmailMessage.Create(inquiry.Email, reply, emailOptions.Value.ReplyToAddress), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue emails for catering inquiry {InquiryId}", notification.InquiryId);
        }
    }
}
