namespace Template.Core.Abstractions;

public interface IEmailSender
{
    /// <summary>
    /// Sends an HTML email. <paramref name="inlineImages"/> are embedded in the message
    /// (multipart/related) and referenced from the HTML via <c>cid:{ContentId}</c> — the
    /// only logo-embedding approach mainstream clients (Gmail/Outlook) render reliably.
    /// </summary>
    Task SendAsync(string to, string subject, string htmlBody, IReadOnlyList<EmailInlineImage>? inlineImages = null);
}

/// <summary>An image embedded in an email and referenced from the HTML as <c>cid:{ContentId}</c>.</summary>
public sealed record EmailInlineImage(string ContentId, string FileName, byte[] Content, string MediaType);
