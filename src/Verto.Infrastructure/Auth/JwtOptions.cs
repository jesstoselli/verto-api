namespace Verto.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "verto";
    public string Audience { get; set; } = "verto-app";

    /// <summary>
    /// Chave HMAC-SHA256 pra assinar access tokens. MÍNIMO 32 caracteres.
    /// Em prod, vem de env var (Jwt__Key). Em dev, do appsettings.Development.json.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 90;
}
