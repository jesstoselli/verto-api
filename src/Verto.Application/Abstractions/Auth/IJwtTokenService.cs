namespace Verto.Application.Abstractions.Auth;

public readonly record struct AccessTokenResult(string Token, DateTime ExpiresAt);

public interface IJwtTokenService
{
    /// <summary>
    /// Gera um access token JWT assinado HS256.
    /// Claims: sub (userId), email, jti, iat, nbf, exp.
    /// Validade controlada pelo JwtOptions:AccessTokenMinutes.
    /// </summary>
    AccessTokenResult GenerateAccessToken(Guid userId, string email);

    /// <summary>
    /// Gera um refresh token: tupla (token raw enviado ao cliente, hash SHA-256
    /// pra persistir no banco, expiração em UTC). Validade controlada pelo
    /// JwtOptions:RefreshTokenDays.
    /// </summary>
    (string Token, string Hash, DateTime ExpiresAt) GenerateRefreshToken();

    /// <summary>
    /// Calcula o hash SHA-256 de um refresh token raw — usado pra comparar
    /// com o que está persistido na tabela refresh_tokens.
    /// </summary>
    string ComputeTokenHash(string rawToken);
}
