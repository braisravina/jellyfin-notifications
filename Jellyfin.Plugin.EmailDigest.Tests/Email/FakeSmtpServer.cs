using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Tests.Email;

/// <summary>
/// A disposable in-process SMTP capture server bound to loopback. It never relays anything: messages
/// are only recorded in memory. Uses a freshly generated self-signed certificate.
/// </summary>
internal sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly X509Certificate2 _certificate;
    private readonly Task _acceptLoop;

    public FakeSmtpServer(bool implicitTls = false)
    {
        ImplicitTls = implicitTls;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        _certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public bool ImplicitTls { get; }

    public bool AdvertiseStartTls { get; init; }

    public bool RequireAuth { get; init; }

    public string User { get; init; } = "mailer";

    public string Password { get; init; } = "correct-horse";

    public bool RejectRecipients { get; init; }

    public bool RejectSender { get; init; }

    public bool HangBeforeGreeting { get; init; }

    public ConcurrentQueue<ReceivedMessage> Messages { get; } = new();

    public ConcurrentQueue<string> Commands { get; } = new();

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _certificate.Dispose();
        _stop.Dispose();
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
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                Stream stream = client.GetStream();
                if (ImplicitTls)
                {
                    var ssl = new SslStream(stream);
                    await ssl.AuthenticateAsServerAsync(_certificate);
                    stream = ssl;
                }

                if (HangBeforeGreeting)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }

                await ConverseAsync(client, stream);
            }
            catch (Exception)
            {
                // A client that gives up mid-conversation is expected in failure tests.
            }
        }
    }

    private async Task ConverseAsync(TcpClient client, Stream stream)
    {
        var encrypted = stream is SslStream;
        var reader = new StreamReader(stream, Encoding.Latin1);
        var writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };
        var authenticated = false;
        string? from = null;
        var recipients = new List<string>();

        await writer.WriteLineAsync("220 fake.local ESMTP");

        while (!_stop.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(_stop.Token);
            if (line is null)
            {
                return;
            }

            Commands.Enqueue(line);
            var upper = line.ToUpperInvariant();

            if (upper.StartsWith("EHLO", StringComparison.Ordinal) || upper.StartsWith("HELO", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("250-fake.local");
                await writer.WriteLineAsync("250-8BITMIME");
                await writer.WriteLineAsync("250-SMTPUTF8");
                if (AdvertiseStartTls && !encrypted)
                {
                    await writer.WriteLineAsync("250-STARTTLS");
                }

                if (RequireAuth)
                {
                    await writer.WriteLineAsync("250-AUTH PLAIN");
                }

                await writer.WriteLineAsync("250 SIZE 10485760");
            }
            else if (upper == "STARTTLS" && AdvertiseStartTls && !encrypted)
            {
                await writer.WriteLineAsync("220 Ready to start TLS");
                var ssl = new SslStream(stream);
                await ssl.AuthenticateAsServerAsync(_certificate);
                stream = ssl;
                encrypted = true;
                reader = new StreamReader(stream, Encoding.Latin1);
                writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };
            }
            else if (upper.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string payload;
                if (parts.Length > 2)
                {
                    payload = parts[2];
                }
                else
                {
                    await writer.WriteLineAsync("334 ");
                    payload = await reader.ReadLineAsync(_stop.Token) ?? string.Empty;
                }

                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0');
                var ok = decoded.Length == 3 && decoded[1] == User && decoded[2] == Password;
                authenticated = ok;
                await writer.WriteLineAsync(ok ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication credentials invalid");
            }
            else if (upper.StartsWith("MAIL FROM", StringComparison.Ordinal))
            {
                if (RequireAuth && !authenticated)
                {
                    await writer.WriteLineAsync("530 5.7.0 Authentication required");
                }
                else if (RejectSender)
                {
                    await writer.WriteLineAsync("553 5.1.8 Sender address rejected");
                }
                else
                {
                    from = line;
                    recipients.Clear();
                    await writer.WriteLineAsync("250 OK");
                }
            }
            else if (upper.StartsWith("RCPT TO", StringComparison.Ordinal))
            {
                if (RejectRecipients)
                {
                    await writer.WriteLineAsync("550 5.1.1 User unknown");
                }
                else
                {
                    recipients.Add(line);
                    await writer.WriteLineAsync("250 OK");
                }
            }
            else if (upper == "DATA")
            {
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                var data = new StringBuilder();
                while (true)
                {
                    var dataLine = await reader.ReadLineAsync(_stop.Token);
                    if (dataLine is null)
                    {
                        return;
                    }

                    if (dataLine == ".")
                    {
                        break;
                    }

                    data.Append(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine).Append("\r\n");
                }

                Messages.Enqueue(new ReceivedMessage(from ?? string.Empty, recipients.ToArray(), Encoding.Latin1.GetBytes(data.ToString()), encrypted));
                await writer.WriteLineAsync("250 2.0.0 Queued");
            }
            else if (upper == "QUIT")
            {
                await writer.WriteLineAsync("221 Bye");
                return;
            }
            else if (upper is "RSET" or "NOOP")
            {
                await writer.WriteLineAsync("250 OK");
            }
            else
            {
                await writer.WriteLineAsync("502 Command not implemented");
            }
        }
    }

    internal sealed record ReceivedMessage(string MailFrom, string[] RcptTo, byte[] Raw, bool WasEncrypted);
}
