using Ideax.Entities;

namespace Ideax.Services.Token;

public interface ITokenService
{
    string CreateToken(User user, IEnumerable<string> roles);
}
