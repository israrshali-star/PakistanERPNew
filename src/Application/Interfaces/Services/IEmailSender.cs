namespace PakistanAccountingERP.Application.Interfaces.Services;

public record EmailAttachment(string FileName, byte[] Content, string ContentType);

public record EmailMessage(
    string ToEmail,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public record EmailSendResult(bool Success, string? Message);

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    bool IsConfigured { get; }
}
