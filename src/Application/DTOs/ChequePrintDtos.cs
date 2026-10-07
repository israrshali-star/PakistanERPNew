namespace PakistanAccountingERP.Application.DTOs;

public record ChequePrintDto(
    string PayeeName,
    DateTime ChequeDate,
    decimal Amount,
    string AmountInWords,
    string AmountFigures,
    string? ChequeNumber,
    ChequePageOrientation Orientation = ChequePageOrientation.Landscape,
    float OffsetXmm = 0,
    float OffsetYmm = 0,
    float DateOffsetXmm = 0,
    float DateOffsetYmm = 0,
    float PayWordsOffsetXmm = 0,
    float PayWordsOffsetYmm = 0);

public enum ChequePayeeMode
{
    Party = 0,
    Cash = 1,
    Text = 2
}

public enum ChequePageOrientation
{
    Landscape = 0,
    Portrait = 1
}
