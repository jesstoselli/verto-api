namespace Verto.Application.UseCases.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Single-user app on a public host: signup stays closed except while
    /// creating the owner's account.
    /// </summary>
    public bool AllowSignup { get; set; }
}
