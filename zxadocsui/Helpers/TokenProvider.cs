using System.IdentityModel.Tokens.Jwt;

namespace zxadocsui.Helpers;

public class TokenProvider
{
    public static Dictionary<string, string> GetTokenClaims(string token)
    {
        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(token))
            return new Dictionary<string, string>();

        var jwt = handler.ReadJwtToken(token);

        return jwt.Claims.ToDictionary(c => c.Type, c => c.Value);
    }

}
