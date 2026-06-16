namespace PakistanAccountingERP.Application.DTOs;

public record DeliveryChallanPrintDto(
    string InvoiceNumber,
    DateTime InvoiceDate,
    string SellerName,
    string? SellerAddress,
    string? SellerPhone,
    string BuyerName,
    string? BuyerAddress,
    string? BuyerProvince,
    string? BuyerNtn,
    string? BuyerCnic,
    DateTime PrintedAt,
    IReadOnlyList<DeliveryChallanPrintLineDto> Lines,
    decimal TransportationChargesReceive = 0m,
    int CompanyId = 0);

public record DeliveryChallanPrintLineDto(
    int LineNo,
    string ItemDescription,
    string? LotNo,
    string? StackNo,
    decimal Cartons,
    decimal Quantity,
    string? Unit,
    string? CartonDescription = null,
    decimal? Amount = null,
    bool IsTransportation = false);
