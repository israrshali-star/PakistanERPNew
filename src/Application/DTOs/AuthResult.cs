namespace PakistanAccountingERP.Application.DTOs;

public record AuthResult(bool Succeeded, string? ErrorMessage = null)
{
    public static AuthResult Success() => new(true);
    public static AuthResult Failure(string message) => new(false, message);
}
