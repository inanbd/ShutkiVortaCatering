using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ShutkiVorta.IntegrationTests.Infrastructure;

/// <summary>
/// Minimal plain-text SMTP server (EHLO, AUTH PLAIN, MAIL, RCPT, DATA, QUIT) for exercising the real MailKit transport.
/// Recipients containing "reject" are refused with 550; a wrong password gets 535.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, 0); // also reachable as 127.0.0.2 ("another machine" for TLS tests)
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;

    public FakeSmtpServer(string userName, string password)
    {
        UserName = userName;
        Password = password;
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public string UserName { get; }
    public string Password { get; }
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public ConcurrentQueue<ReceivedEmail> Received { get; } = new();

    public async Task<ReceivedEmail> WaitForEmailAsync(Func<ReceivedEmail, bool> match, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Received.FirstOrDefault(match) is { } email)
            {
                return email;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No matching email reached the fake SMTP server within {timeout}.");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };

        string? authenticatedAs = null;
        string? from = null;
        var recipients = new List<string>();

        await writer.WriteLineAsync("220 fake.smtp.test ESMTP ready");
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-fake.smtp.test greets you");
                    await writer.WriteLineAsync("250-AUTH PLAIN");
                    await writer.WriteLineAsync("250 8BITMIME");
                    break;
                case "HELO":
                    await writer.WriteLineAsync("250 fake.smtp.test");
                    break;
                case "AUTH":
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var payload = parts.Length > 2 ? parts[2] : null;
                    if (payload is null)
                    {
                        await writer.WriteLineAsync("334 ");
                        payload = await reader.ReadLineAsync();
                    }

                    var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(payload ?? string.Empty)).Split('\0');
                    if (credentials.Length == 3 && credentials[1] == UserName && credentials[2] == Password)
                    {
                        authenticatedAs = credentials[1];
                        await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                    }
                    else
                    {
                        await writer.WriteLineAsync("535 5.7.8 Username and Password not accepted");
                    }

                    break;
                case "MAIL":
                    if (authenticatedAs is null)
                    {
                        await writer.WriteLineAsync("530 5.7.0 Authentication required");
                        break;
                    }

                    from = Address(line);
                    recipients.Clear();
                    await writer.WriteLineAsync("250 2.1.0 Ok");
                    break;
                case "RCPT":
                    var to = Address(line);
                    if (to.Contains("reject", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("550 5.1.1 Mailbox unavailable");
                    }
                    else
                    {
                        recipients.Add(to);
                        await writer.WriteLineAsync("250 2.1.5 Ok");
                    }

                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.AppendLine(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine);
                    }

                    Received.Enqueue(new ReceivedEmail(authenticatedAs, from ?? string.Empty, [.. recipients], data.ToString()));
                    await writer.WriteLineAsync($"250 2.0.0 Ok: queued as FAKE{Received.Count:000}");
                    break;
                case "RSET":
                case "NOOP":
                    await writer.WriteLineAsync("250 2.0.0 Ok");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 2.0.0 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("502 5.5.2 Command not recognized");
                    break;
            }
        }
    }

    private static string Address(string line)
    {
        var start = line.IndexOf('<');
        var end = line.IndexOf('>');
        return start >= 0 && end > start ? line[(start + 1)..end] : line[(line.IndexOf(':') + 1)..].Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }
}

public sealed record ReceivedEmail(string? AuthenticatedAs, string From, IReadOnlyList<string> Recipients, string Data);
