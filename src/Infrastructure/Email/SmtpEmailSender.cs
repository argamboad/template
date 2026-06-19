using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Template.Core.Abstractions;

namespace Template.Infrastructure.Email;

public class SmtpEmailSender(IOptions<SmtpSettings> options) : IEmailSender
{
    private readonly SmtpSettings _settings = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody,
        IReadOnlyList<EmailInlineImage>? inlineImages = null)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        if (inlineImages is not null)
        {
            foreach (var image in inlineImages)
            {
                var resource = builder.LinkedResources.Add(
                    image.FileName, image.Content, ContentType.Parse(image.MediaType));
                resource.ContentId = image.ContentId; // referenced from HTML as cid:{ContentId}
            }
        }
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.Auto);
        if (!string.IsNullOrEmpty(_settings.Username))
            await client.AuthenticateAsync(_settings.Username, _settings.Password ?? string.Empty);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
