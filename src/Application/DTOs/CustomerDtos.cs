using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.DTOs;

public record CustomerDto(
    int Id,
    string BuyerId,
    string BuyerName,
    decimal OpeningBalance,
    decimal Balance,
    string? Address,
    int? ProvinceId,
    string? ProvinceName,
    int ScenarioId,
    string? ScenarioCode,
    string? Phone,
    string? Mobile,
    string? Email,
    string? NTN,
    string? CNIC,
    string? STRN,
    CustomerType CustomerType,
    InvoiceType InvoiceType,
    decimal? FurtherTaxRate,
    bool IsActive,
    bool HasInvoices);

public record CustomerListItemDto(
    int Id,
    string BuyerId,
    string BuyerName,
    string CustomerType,
    string? ProvinceName,
    string? NTN,
    string? Phone,
    decimal OpeningBalance,
    decimal Balance,
    bool IsActive);

public class CustomerSaveRequest
{
    public int? Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public string? Address { get; set; }
    public int? ProvinceId { get; set; }
    public int ScenarioId { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? NTN { get; set; }
    public string? CNIC { get; set; }
    public string? STRN { get; set; }
    public CustomerType CustomerType { get; set; } = CustomerType.Registered;
    public InvoiceType InvoiceType { get; set; } = InvoiceType.SalesInvoice;
    /// <summary>Optional override for SN002 further tax % (e.g. 2). Leave null for company default.</summary>
    public decimal? FurtherTaxRate { get; set; }
    public bool IsActive { get; set; } = true;
}

public record CustomerSaveResult(bool Success, string? Message, CustomerDto? Customer);

public record NextBuyerIdDto(string BuyerId);

public record CustomerLedgerEntryDto(
    DateTime Date,
    string Reference,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    decimal PendingCredit = 0m);

public record CustomerLedgerDto(
    CustomerDto Customer,
    IReadOnlyList<CustomerLedgerEntryDto> Entries,
    decimal ClosingBalance);

public record CustomerStatementDto(
    CustomerDto Customer,
    DateTime FromDate,
    DateTime ToDate,
    decimal OpeningBalance,
    IReadOnlyList<CustomerLedgerEntryDto> Entries,
    decimal ClosingBalance);
