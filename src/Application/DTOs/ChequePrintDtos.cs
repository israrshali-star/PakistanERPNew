namespace PakistanAccountingERP.Application.DTOs;

public record ChequePrintDto(
    string PayeeName,
    DateTime ChequeDate,
    decimal Amount,
    string AmountInWords,
    string AmountFigures,
    string? ChequeNumber,
    ChequePageOrientation Orientation = ChequePageOrientation.Landscape);

public enum ChequePayeeMode
{
    Party = 0,
    Cash = 1
}

public enum ChequePageOrientation
{
    Landscape = 0,
    Portrait = 1
}
