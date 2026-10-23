using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.Domain.Integrations.DevelopmentMail;

/// <summary>
/// The local fake for transactional email (external.md): delivers over SMTP to the Mailpit container, where a
/// developer reads it in the dashboard. Nothing leaves the machine. Not for production; Azure Communication Services
/// replaces it in PP13.1.
/// </summary>
internal sealed class SmtpDevelopmentMailGateway(IOptions<DevelopmentMailOptions> options) : IEmailGateway
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        using var mail = new MailMessage(new MailAddress(settings.From), new MailAddress(message.To))
        {
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.Headers.Add("Message-ID", $"<{message.Id:N}@pantrypress.localhost>");

        if (message.HtmlBody is not null)
        {
            mail.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = false,
            UseDefaultCredentials = false,
        };

        // SmtpClient.Timeout covers only the synchronous Send, so the async send is bounded by a token instead.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.SendTimeout);

        // Provider exceptions stay here: SmtpFailedRecipientException quotes the recipient.
        try
        {
            await smtp.SendMailAsync(mail, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailDeliveryException(message.Id, isTransient: true, "the mail catcher did not answer in time");
        }
        catch (SmtpFailedRecipientException ex)
        {
            throw new EmailDeliveryException(message.Id, IsTransient(ex.StatusCode), $"recipient refused ({(int)ex.StatusCode})");
        }
        catch (SmtpException ex)
        {
            throw new EmailDeliveryException(message.Id, IsTransient(ex.StatusCode), $"SMTP failure ({(int)ex.StatusCode})");
        }
    }

    // 4xx replies are temporary by definition (RFC 5321); GeneralFailure is a connection that never completed.
    private static bool IsTransient(SmtpStatusCode code) =>
        code == SmtpStatusCode.GeneralFailure || (int)code is >= 400 and < 500;
}
