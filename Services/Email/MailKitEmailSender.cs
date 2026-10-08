using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Ideax.Services.Email;

public class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(
        IOptions<EmailOptions> options,
        ILogger<MailKitEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody)
    {
        var message = new MimeMessage();

        message.From.Add(
            MailboxAddress.Parse(_options.From));

        message.To.Add(
            MailboxAddress.Parse(to));

        message.Subject = subject;

        var body = new BodyBuilder
        {
            HtmlBody = htmlBody
        };

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();

        try
        {
            SecureSocketOptions socketOptions;

          
                // Gmail SSL/TLS immediately upon connection
                socketOptions = SecureSocketOptions.SslOnConnect;
 

            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                socketOptions);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(
                    _options.Username,
                    _options.Password ?? string.Empty);
            }

            await client.SendAsync(message);

            _logger.LogInformation(
                "MailKit: Email sent to {To} (subject: {Subject})",
                to,
                subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "MailKit: Failed to send email to {To}",
                to);

            throw;
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(true);
                }
                catch
                {
                    // Ignore disconnect errors
                }
            }
        }
    }
}