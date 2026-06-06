namespace PakistanAccountingERP.Application.DTOs;

public record VendorDto(
    int Id,
    string VendorCode,
    string VendorName,
    decimal OpeningBalance,
    decimal Balance,
    string? Address,
    int? ProvinceId,
    string? ProvinceName,
    string? Phone,
    string? Email,
    string? NTN,
    decimal DefaultSalesTaxRate,
    bool IsActive,
    bool HasBills);

public record VendorListItemDto(
    int Id,
    string VendorCode,
    string VendorName,
    string? ProvinceName,
    string? NTN,
    string? Phone,
    decimal DefaultSalesTaxRate,
    decimal OpeningBalance,
    decimal Balance,
    bool IsActive);

public record VendorSaveRequest(
    int? Id,
    string VendorCode,
    string VendorName,
    decimal OpeningBalance,
    string? Address,
    int? ProvinceId,
    string? Phone,
    string? Email,
    string? NTN,
    decimal DefaultSalesTaxRate,
    bool IsActive);

public record VendorSaveResult(bool Success, string? Message, VendorDto? Vendor);

public record NextVendorCodeDto(string VendorCode);

public record VendorLedgerEntryDto(
    DateTime Date,
    string Reference,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public record VendorLedgerDto(
    VendorDto Vendor,
    IReadOnlyList<VendorLedgerEntryDto> Entries,
    decimal ClosingBalance);

public record VendorStatementDto(
    VendorDto Vendor,
    DateTime FromDate,
    DateTime ToDate,
    decimal OpeningBalance,
    IReadOnlyList<VendorLedgerEntryDto> Entries,
    decimal ClosingBalance);
