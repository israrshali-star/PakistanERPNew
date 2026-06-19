using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;

namespace PakistanAccountingERP.Infrastructure.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly ICompanyMessagingSettingsService _messagingSettings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        ICompanyMessagingSettingsService messagingSettings,
        ILogger<SmtpEmailSender> logger)
    {
        _messagingSettings = messagingSettings;
        _logger = logger;
    }

    public bool IsConfigured => _messagingSettings.IsSmtpConfiguredAsync().GetAwaiter().GetResult();

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
        _messagingSettings.IsSmtpConfiguredAsync(cancellationToken);

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = await _messagingSettings.GetSmtpSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return new EmailSendResult(
                false,
                "Email is not configured. Set up SMTP in Company Settings (Settings → Company Settings → Email Settings).");
        }

        if (string.IsNullOrWhiteSpace(message.ToEmail))
        {
            return new EmailSendResult(false, "Recipient email is required.");
        }

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
            mime.To.Add(MailboxAddress.Parse(message.ToEmail.Trim()));
            mime.Subject = message.Subject;

            var body = new BodyBuilder
            {
                HtmlBody = message.HtmlBody,
                TextBody = message.PlainTextBody
            };

            if (message.Attachments is not null)
            {
                foreach (var attachment in message.Attachments)
                {
                    body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
                }
            }

            mime.Body = body.ToMessageBody();

            using var client = new SmtpClient();
            var socketOptions = ResolveSocketOptions(settings);
            await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return new EmailSendResult(true, "Email sent successfully.");
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP authentication failed for {Username}", settings.Username);
            return new EmailSendResult(
                false,
                "SMTP login failed. Check the SMTP username and app password in Company Settings → Email Settings.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", message.ToEmail);
            return new EmailSendResult(false, "Could not send email: " + ex.Message);
        }
    }

    private static SecureSocketOptions ResolveSocketOptions(ResolvedSmtpSettings settings)
    {
        if (settings.Port == 465)
        {
            return SecureSocketOptions.SslOnConnect;
        }

        if (settings.UseSsl)
        {
            return SecureSocketOptions.StartTls;
        }

        return SecureSocketOptions.Auto;
    }
}
