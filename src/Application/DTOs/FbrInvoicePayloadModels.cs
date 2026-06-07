using System.Text.Json.Serialization;

namespace PakistanAccountingERP.Application.DTOs;

/// <summary>
/// FBR e-invoice API payload shape (flat seller/buyer fields + item lines).
/// </summary>
public sealed class FbrInvoicePayload
{
    [JsonPropertyName("invoiceType")]
    public string InvoiceType { get; init; } = string.Empty;

    [JsonPropertyName("invoiceDate")]
    public string InvoiceDate { get; init; } = string.Empty;

    [JsonPropertyName("sellerNTNCNIC")]
    public string SellerNtnCnic { get; init; } = string.Empty;

    [JsonPropertyName("sellerBusinessName")]
    public string SellerBusinessName { get; init; } = string.Empty;

    [JsonPropertyName("sellerProvince")]
    public string? SellerProvince { get; init; }

    [JsonPropertyName("sellerAddress")]
    public string? SellerAddress { get; init; }

    [JsonPropertyName("buyerNTNCNIC")]
    public string BuyerNtnCnic { get; init; } = string.Empty;

    [JsonPropertyName("buyerBusinessName")]
    public string BuyerBusinessName { get; init; } = string.Empty;

    [JsonPropertyName("buyerProvince")]
    public string? BuyerProvince { get; init; }

    [JsonPropertyName("buyerAddress")]
    public string? BuyerAddress { get; init; }

    [JsonPropertyName("buyerRegistrationType")]
    public string BuyerRegistrationType { get; init; } = string.Empty;

    [JsonPropertyName("invoiceRefNo")]
    public string InvoiceRefNo { get; init; } = string.Empty;

    [JsonPropertyName("scenarioId")]
    public string ScenarioId { get; init; } = string.Empty;

    [JsonPropertyName("items")]
    public IReadOnlyList<FbrInvoiceItemPayload> Items { get; init; } = Array.Empty<FbrInvoiceItemPayload>();
}

public sealed class FbrInvoiceItemPayload
{
    [JsonPropertyName("hsCode")]
    public string? HsCode { get; init; }

    [JsonPropertyName("productDescription")]
    public string ProductDescription { get; init; } = string.Empty;

    [JsonPropertyName("rate")]
    public string Rate { get; init; } = string.Empty;

    [JsonPropertyName("uoM")]
    public string? UoM { get; init; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; init; }

    [JsonPropertyName("totalValues")]
    public decimal TotalValues { get; init; }

    [JsonPropertyName("valueSalesExcludingST")]
    public decimal ValueSalesExcludingSt { get; init; }

    [JsonPropertyName("fixedNotifiedValueOrRetailPrice")]
    public decimal FixedNotifiedValueOrRetailPrice { get; init; }

    [JsonPropertyName("salesTaxApplicable")]
    public decimal SalesTaxApplicable { get; init; }

    [JsonPropertyName("salesTaxWithheldAtSource")]
    public decimal SalesTaxWithheldAtSource { get; init; }

    [JsonPropertyName("extraTax")]
    public string ExtraTax { get; init; } = string.Empty;

    [JsonPropertyName("furtherTax")]
    public decimal FurtherTax { get; init; }

    [JsonPropertyName("sroScheduleNo")]
    public string? SroScheduleNo { get; init; }

    [JsonPropertyName("fedPayable")]
    public decimal FedPayable { get; init; }

    [JsonPropertyName("discount")]
    public decimal Discount { get; init; }

    [JsonPropertyName("saleType")]
    public string SaleType { get; init; } = string.Empty;

    [JsonPropertyName("sroItemSerialNo")]
    public string? SroItemSerialNo { get; init; }
}
