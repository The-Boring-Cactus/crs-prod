using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace Server.Core;

// Sends transactional email (password reset, dashboard share links) through
// whichever SMTP server was configured in the Setup Wizard or the Email
// settings tab. Reads config fresh on every call — same pattern as
// DatabasePersistence.CreateConnection() — so a settings change takes effect
// immediately without restarting the server.
public static class EmailService
{
    public static bool IsConfigured(SmtpConfig smtp) =>
        smtp != null && smtp.IsConfigured
        && !string.IsNullOrWhiteSpace(smtp.Host)
        && !string.IsNullOrWhiteSpace(smtp.FromAddress);

    public static async Task<(bool success, string message)> SendEmailAsync(string toAddress, string subject, string htmlBody)
    {
        var config = SetupConfig.Load();
        var smtp = config.Smtp;

        if (!IsConfigured(smtp))
            return (false, "SMTP is not configured. An administrator needs to configure it under Settings > Email.");

        if (string.IsNullOrWhiteSpace(toAddress))
            return (false, "Missing recipient email address.");

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(smtp.FromAddress));
            message.To.Add(MailboxAddress.Parse(toAddress));
            message.Subject = subject;
            message.Body = new TextPart(TextFormat.Html) { Text = htmlBody };

            using var client = new SmtpClient();
            var port = smtp.Port > 0 ? smtp.Port : 587;
            var socketOptions = smtp.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
            await client.ConnectAsync(smtp.Host, port, socketOptions);

            if (!string.IsNullOrEmpty(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return (true, "Email sent");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
