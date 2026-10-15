using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Verto.Application.Abstractions.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Verto.Infrastructure.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.Key) || _options.Key.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key is missing or too short (minimum 32 characters).");
        }

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
    }

    public AccessTokenResult GenerateAccessToken(Guid userId, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };

        var unroundedExpiry = now.AddMinutes(_options.AccessTokenMinutes);
        var expiresAt = new DateTime(
            unroundedExpiry.Ticks - (unroundedExpiry.Ticks % TimeSpan.TicksPerSecond),
            DateTimeKind.Utc);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: _signingCredentials);

        return new AccessTokenResult(_tokenHandler.WriteToken(token), expiresAt);
    }

    public (string Token, string Hash, DateTime ExpiresAt) GenerateRefreshToken()
    {
        // 32 bytes random => Base64Url => ~43 chars (no padding)
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(bytes);
        var hash = ComputeTokenHash(token);
        var expiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);

        return (token, hash, expiresAt);
    }

    public string ComputeTokenHash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
