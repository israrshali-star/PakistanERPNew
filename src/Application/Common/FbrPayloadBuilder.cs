using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Domain.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PakistanAccountingERP.Application.Common;

public static class FbrPayloadBuilder
{
    public const string SystemGeneratedFooter =
        "THIS IS SYSTEM GENERATED INVOICE DOES NOT REQUIRE SIGNATURE AND COMPANY STAMP.";

    public const string DefaultSaleType = "Goods at standard rate (default)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null
    };

    public static string BuildJson(FbrSubmissionRequest request) =>
        JsonSerializer.Serialize(BuildPayload(request), JsonOptions);

    public static FbrInvoicePayload BuildPayload(FbrSubmissionRequest request) =>
        new()
        {
            InvoiceType = MapInvoiceType(request.InvoiceType),
            InvoiceDate = request.InvoiceDate.ToString("yyyy-MM-dd"),
            SellerNtnCnic = ResolveNtnCnic(request.Seller.Ntn, request.Seller.Cnic),
            SellerBusinessName = request.Seller.Name,
            SellerProvince = request.Seller.Province,
            SellerAddress = request.Seller.Address,
            BuyerNtnCnic = ResolveNtnCnic(request.Buyer.Ntn, request.Buyer.Cnic),
            BuyerBusinessName = request.Buyer.Name,
            BuyerProvince = NormalizeBuyerProvince(request.Buyer.Province),
            BuyerAddress = request.Buyer.Address,
            BuyerRegistrationType = request.BuyerRegistrationType,
            InvoiceRefNo = request.InvoiceNumber,
            ScenarioId = request.ScenarioCode,
            Items = request.Lines.Select(MapLine).ToList()
        };

    public static object BuildObject(FbrSubmissionRequest request) => BuildPayload(request);

    public static string MapBuyerRegistrationType(CustomerType customerType) =>
        customerType == CustomerType.Registered ? "Registered" : "Unregistered";

    public static string MapSaleType(string? scenarioCode) =>
        scenarioCode switch
        {
            "SN002" => "Goods at standard rate (default)",
            "SN001" => DefaultSaleType,
            _ => DefaultSaleType
        };

    private static FbrInvoiceItemPayload MapLine(FbrSubmissionLineRequest line)
    {
        var valueExcludingSt = Math.Round(Math.Max(0m, line.Quantity * line.Price - line.Discount), 2);
        var salesTax = line.FurtherTaxAmount > 0m
            ? Math.Round(line.SalesTaxAmount, 2)
            : Math.Round(line.TaxAmount, 2);
        var furtherTax = Math.Round(line.FurtherTaxAmount, 2);
        var totalValues = Math.Round(line.LineTotal, 2);

        return new FbrInvoiceItemPayload
        {
            HsCode = line.HsCode,
            ProductDescription = line.ProductDescription,
            Rate = FormatTaxRatePercent(line.TaxRate),
            UoM = line.Unit,
            Quantity = Math.Round(line.Quantity, 2),
            TotalValues = totalValues,
            ValueSalesExcludingSt = valueExcludingSt,
            FixedNotifiedValueOrRetailPrice = 0m,
            SalesTaxApplicable = salesTax,
            SalesTaxWithheldAtSource = 0m,
            ExtraTax = string.Empty,
            FurtherTax = furtherTax,
            SroScheduleNo = null,
            FedPayable = 0m,
            Discount = Math.Round(line.Discount, 2),
            SaleType = line.SaleType,
            SroItemSerialNo = null
        };
    }

    private static string MapInvoiceType(InvoiceType invoiceType) =>
        invoiceType switch
        {
            InvoiceType.DebitNote => "Debit Note",
            InvoiceType.CreditNote => "Credit Note",
            _ => "Sale Invoice"
        };

    private static string ResolveNtnCnic(string? ntn, string? cnic)
    {
        if (!string.IsNullOrWhiteSpace(ntn))
        {
            return ntn.Trim();
        }

        return !string.IsNullOrWhiteSpace(cnic) ? cnic.Trim() : string.Empty;
    }

    private static string? NormalizeBuyerProvince(string? province) =>
        string.IsNullOrWhiteSpace(province) ? province : province.Trim().ToUpperInvariant();

    private static string FormatTaxRatePercent(decimal taxRate) =>
        taxRate % 1 == 0 ? $"{(int)taxRate}%" : $"{taxRate:0.##}%";
}
