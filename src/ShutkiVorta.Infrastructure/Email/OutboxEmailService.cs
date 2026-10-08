using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>Wakes the dispatcher as soon as an email is queued (it also polls, so nothing is ever stranded).</summary>
internal sealed class EmailDispatchSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>Writes emails to the durable outbox table; <see cref="EmailDispatcher"/> delivers them in the background.</summary>
internal sealed class OutboxEmailService(EmailOutboxRepository outbox, EmailDispatchSignal signal) : IEmailService
{
    public async ValueTask QueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        await outbox.EnqueueAsync(message, cancellationToken);
        signal.Notify();
    }
}
