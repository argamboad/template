using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Template.Core.Abstractions;

namespace Template.Infrastructure.Email;

public class SmtpEmailSender(IOptions<SmtpSettings> options) : IEmailSender
{
    private readonly SmtpSettings _settings = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.Auto);
        if (!string.IsNullOrEmpty(_settings.Username))
            await client.AuthenticateAsync(_settings.Username, _settings.Password ?? string.Empty);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
