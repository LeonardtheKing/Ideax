using System.Threading.Tasks;

namespace Ideax.Services.Email;

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
}
