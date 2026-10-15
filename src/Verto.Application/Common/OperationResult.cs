namespace Verto.Application.Common;

/// <summary>
/// Result type pra retornar sucesso ou falha sem usar exceções pra fluxo de controle.
/// Caller decide como tratar (200, 400, 401, 409, etc) baseado no ErrorCode.
/// </summary>
public sealed record OperationResult<T>(
    bool IsSuccess,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Factory methods não-genéricos — permitem que o compilador infira T do argumento,
/// transformando OperationResult&lt;Foo&gt;.Ok(value) em OperationResult.Ok(value).
/// </summary>
public static class OperationResult
{
    public static OperationResult<T> Ok<T>(T value) =>
        new(true, value, null, null);

    public static OperationResult<T> Fail<T>(string code, string message) =>
        new(false, default, code, message);
}
