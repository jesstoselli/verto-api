using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Verto.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Tenta extrair o userId do claim 'sub' do JWT.
    /// Como Program.cs configurou MapInboundClaims=false, o claim mantém o nome original.
    /// </summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(sub, out userId);
    }
}
