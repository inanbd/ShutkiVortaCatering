using System.Threading.Channels;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>In-memory queue drained by <see cref="EmailDispatcher"/>. Keeps SMTP latency out of web requests.</summary>
internal sealed class EmailQueue : IEmailService
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public ValueTask QueueAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(message, cancellationToken);
}
