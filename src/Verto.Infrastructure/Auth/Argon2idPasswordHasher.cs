using System.Security.Cryptography;
using System.Text;
using Verto.Application.Abstractions.Auth;
using Konscious.Security.Cryptography;

namespace Verto.Infrastructure.Auth;

/// <summary>
/// Argon2id password hasher. RFC 9106 recommended params (low-memory profile):
/// memory=64MB, iterations=3, parallelism=4, salt=16B, hash=32B.
/// Format: $argon2id$v=19$m=65536,t=3,p=4${salt_b64}${hash_b64}
/// </summary>
public class Argon2idPasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 3;
    private const int MemorySize = 65536;   // 64 MB
    private const int Parallelism = 4;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeHash(password, salt);

        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        // Format: $argon2id$v=19$m=...,t=...,p=...$salt$hash
        var parts = hash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id")
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = ComputeHash(password, salt);

            // Constant-time comparison previne timing attacks
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] ComputeHash(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Parallelism,
            Iterations = Iterations,
            MemorySize = MemorySize,
        };
        return argon2.GetBytes(HashSize);
    }
}
