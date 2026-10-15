namespace Verto.Application.Abstractions.Auth;

public interface IPasswordHasher
{
    /// <summary>
    /// Hash de senha usando Argon2id. Salt aleatório embutido no resultado.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Compara senha em plaintext com hash previamente gerado.
    /// Retorna false pra qualquer input inválido (sem throw).
    /// </summary>
    bool Verify(string password, string hash);
}
