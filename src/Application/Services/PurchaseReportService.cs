using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class PurchaseReportService : IPurchaseReportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public PurchaseReportService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<PurchaseRegisterReportDto> GetPurchaseRegisterAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildBillQuery(companyId, from, to, request);

        if (request.VendorId.HasValue)
        {
            query = query.Where(b => b.VendorId == request.VendorId.Value);
        }

        string? vendorLabel = null;
        if (request.VendorId.HasValue)
        {
            vendorLabel = await _unitOfWork.Repository<Vendor>()
                .Query()
                .Where(v => v.Id == request.VendorId.Value && v.CompanyId == companyId)
                .Select(v => v.VendorCode + " — " + v.VendorName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var bills = await query
            .OrderBy(b => b.BillDate)
            .ThenBy(b => b.BillNumber)
            .Select(b => new PurchaseRegisterLineDto(
                b.Id,
                b.BillNumber,
                b.RefNo,
                b.BillDate,
                b.Vendor.VendorName,
                b.Status.ToString(),
                b.TotalQuantity,
                b.TaxAmount,
                b.NetAmount))
            .ToListAsync(cancellationToken);

        return new PurchaseRegisterReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            request.VendorId,
            vendorLabel,
            bills.Count,
            bills.Sum(l => l.TotalQuantity),
            bills.Sum(l => l.TaxAmount),
            bills.Sum(l => l.NetAmount),
            bills);
    }

    public async Task<PurchaseByVendorReportDto> GetPurchaseByVendorAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildBillQuery(companyId, from, to, request);

        if (request.VendorId.HasValue)
        {
            query = query.Where(b => b.VendorId == request.VendorId.Value);
        }

        var grouped = await query
            .GroupBy(b => b.VendorId)
            .Select(g => new
            {
                VendorId = g.Key,
                BillCount = g.Count(),
                TotalQuantity = g.Sum(b => b.TotalQuantity),
                TaxAmount = g.Sum(b => b.TaxAmount),
                NetAmount = g.Sum(b => b.NetAmount)
            })
            .ToListAsync(cancellationToken);

        var vendorIds = grouped.Select(g => g.VendorId).Distinct().ToList();
        var vendors = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.VendorCode, v.VendorName })
            .ToListAsync(cancellationToken);
        var vendorLookup = vendors.ToDictionary(v => v.Id);

        var groupedLines = grouped
            .Select(g =>
            {
                vendorLookup.TryGetValue(g.VendorId, out var vendor);
                return new PurchaseByVendorLineDto(
                    g.VendorId,
                    vendor?.VendorCode ?? "—",
                    vendor?.VendorName ?? "—",
                    g.BillCount,
                    g.TotalQuantity,
                    g.TaxAmount,
                    g.NetAmount);
            })
            .OrderBy(l => l.VendorName)
            .ToList();

        return new PurchaseByVendorReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            groupedLines.Count,
            groupedLines.Sum(l => l.TotalQuantity),
            groupedLines.Sum(l => l.TaxAmount),
            groupedLines.Sum(l => l.NetAmount),
            groupedLines);
    }

    public async Task<InputTaxSummaryReportDto> GetInputTaxSummaryAsync(
        PurchaseReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var (companyId, from, to) = ValidateDateRange(request);
        var query = BuildBillQuery(companyId, from, to, request);

        if (request.VendorId.HasValue)
        {
            query = query.Where(b => b.VendorId == request.VendorId.Value);
        }

        var summary = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Quantity = g.Sum(b => b.TotalQuantity),
                Tax = g.Sum(b => b.TaxAmount),
                Net = g.Sum(b => b.NetAmount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new InputTaxSummaryReportDto(
            request.FromDate.Date,
            request.ToDate.Date,
            summary?.Count ?? 0,
            summary?.Quantity ?? 0m,
            summary?.Tax ?? 0m,
            summary?.Net ?? 0m);
    }

    public async Task<IReadOnlyList<PurchaseReportVendorLookupDto>> GetVendorLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive)
            .OrderBy(v => v.VendorName)
            .Select(v => new PurchaseReportVendorLookupDto(v.Id, v.VendorCode, v.VendorName))
            .ToListAsync(cancellationToken);
    }

    public async Task<StackLotTrackingReportDto> GetStackLotTrackingAsync(
        StackLotTrackingRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var lotFilter = request.LotNo?.Trim();
        var stackFilter = request.StackNo?.Trim();

        string? itemLabel = null;
        if (request.ItemId.HasValue)
        {
            itemLabel = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.Id == request.ItemId.Value && i.CompanyId == companyId)
                .Select(i => i.ItemCode + " — " + i.ItemName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var purchaseQuery = _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId != null);

        if (request.ItemId.HasValue)
        {
            purchaseQuery = purchaseQuery.Where(l => l.ItemId == request.ItemId.Value);
        }

        if (!string.IsNullOrWhiteSpace(lotFilter))
        {
            purchaseQuery = purchaseQuery.Where(l =>
                l.LotNo == lotFilter
                || ((l.LotNo == null || l.LotNo == "") && l.Item!.LotNo == lotFilter));
        }

        if (!string.IsNullOrWhiteSpace(stackFilter))
        {
            purchaseQuery = purchaseQuery.Where(l =>
                l.StackNo == stackFilter
                || ((l.StackNo == null || l.StackNo == "") && l.Item!.StackNo == stackFilter));
        }

        var purchases = (await purchaseQuery
            .Select(l => new StackLotPurchaseRow(
                l.ItemId!.Value,
                l.Item!.ItemCode,
                l.Item.ItemName,
                l.StackNo,
                l.LotNo,
                l.Item.StackNo,
                l.Item.LotNo,
                l.Quantity,
                l.Cartons,
                l.Amount,
                l.VendorBill.BillNumber,
                l.VendorBill.BillDate))
            .ToListAsync(cancellationToken))
            .Select(NormalizePurchaseRow)
            .ToList();

        var salesQuery = _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status != InvoiceStatus.Cancelled);

        if (request.ItemId.HasValue)
        {
            salesQuery = salesQuery.Where(l => l.ItemId == request.ItemId.Value);
        }

        if (!string.IsNullOrWhiteSpace(lotFilter))
        {
            salesQuery = salesQuery.Where(l =>
                l.LotNo == lotFilter
                || ((l.LotNo == null || l.LotNo == "") && l.Item.LotNo == lotFilter));
        }

        if (!string.IsNullOrWhiteSpace(stackFilter))
        {
            salesQuery = salesQuery.Where(l =>
                l.StackNo == stackFilter
                || ((l.StackNo == null || l.StackNo == "") && l.Item.StackNo == stackFilter));
        }

        var sales = (await salesQuery
            .Select(l => new StackLotSaleRow(
                l.ItemId,
                l.Item.ItemCode,
                l.Item.ItemName,
                l.StackNo,
                l.LotNo,
                l.Item.StackNo,
                l.Item.LotNo,
                l.Quantity,
                l.Cartons,
                l.LineTotal,
                l.SalesInvoice.InvoiceNumber,
                l.SalesInvoice.InvoiceDate,
                l.SalesInvoice.InvoiceType,
                l.SalesInvoice.Status))
            .ToListAsync(cancellationToken))
            .Select(NormalizeSaleRow)
            .ToList();

        var keys = purchases
            .Select(p => StackLotKey.From(p.ItemId, p.StackNo, p.LotNo))
            .Concat(sales.Select(s => StackLotKey.From(s.ItemId, s.StackNo, s.LotNo)))
            .Distinct()
            .OrderBy(k => k.ItemId)
            .ThenBy(k => k.StackNo)
            .ThenBy(k => k.LotNo)
            .ToList();

        var lines = new List<StackLotTrackingLineDto>();

        foreach (var key in keys)
        {
            var itemPurchases = purchases.Where(p => key.Matches(p.ItemId, p.StackNo, p.LotNo)).ToList();
            var itemSales = sales.Where(s => key.Matches(s.ItemId, s.StackNo, s.LotNo)).ToList();

            if (itemPurchases.Count == 0 && itemSales.Count == 0)
            {
                continue;
            }

            var itemCode = itemPurchases.FirstOrDefault()?.ItemCode
                ?? itemSales.First().ItemCode;
            var itemName = itemPurchases.FirstOrDefault()?.ItemName
                ?? itemSales.First().ItemName;

            var purchasedCartons = itemPurchases.Sum(p => p.Cartons);
            var purchasedWeight = itemPurchases.Sum(p => p.Quantity);
            var purchasedAmount = itemPurchases.Sum(p => p.Amount);

            var soldCartons = itemSales.Sum(s => SignedCartons(s));
            var soldWeight = itemSales.Sum(s => SignedWeight(s));
            var soldAmount = itemSales.Sum(s => SignedAmount(s));

            var remainingCartons = Math.Round(purchasedCartons - soldCartons, 2);
            var remainingWeight = Math.Round(purchasedWeight - soldWeight, 2);

            var movements = itemPurchases
                .Select(p => new StackLotMovementDto(
                    "Purchase",
                    p.BillNumber,
                    p.BillDate,
                    p.Cartons,
                    p.Quantity,
                    p.Amount))
                .Concat(itemSales.Select(s => new StackLotMovementDto(
                    SaleMovementLabel(s),
                    s.InvoiceNumber,
                    s.InvoiceDate,
                    SignedCartons(s),
                    SignedWeight(s),
                    SignedAmount(s))))
                .OrderBy(m => m.Date)
                .ThenBy(m => m.ReferenceNumber)
                .ToList();

            lines.Add(new StackLotTrackingLineDto(
                key.ItemId,
                itemCode,
                itemName,
                key.StackNo,
                key.LotNo,
                purchasedCartons,
                purchasedWeight,
                purchasedAmount,
                soldCartons,
                soldWeight,
                soldAmount,
                remainingCartons,
                remainingWeight,
                movements));
        }

        return new StackLotTrackingReportDto(
            request.ItemId,
            itemLabel,
            lotFilter,
            stackFilter,
            lines.Sum(l => l.PurchasedCartons),
            lines.Sum(l => l.PurchasedWeight),
            lines.Sum(l => l.PurchasedAmount),
            lines.Sum(l => l.SoldCartons),
            lines.Sum(l => l.SoldWeight),
            lines.Sum(l => l.SoldAmount),
            lines.Sum(l => l.RemainingCartons),
            lines.Sum(l => l.RemainingWeight),
            lines);
    }

    public async Task<IReadOnlyList<StackLotReportItemLookupDto>> GetStackLotItemLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive)
            .OrderBy(i => i.ItemName)
            .Select(i => new StackLotReportItemLookupDto(i.Id, i.ItemCode, i.ItemName))
            .ToListAsync(cancellationToken);
    }

    public async Task<StackLotFilterLookupDto> GetStackLotFilterLookupsAsync(
        int? itemId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var purchaseQuery = _unitOfWork.Repository<VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId != null);

        var salesQuery = _unitOfWork.Repository<SalesInvoiceLine>()
            .Query()
            .Where(l => l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status != InvoiceStatus.Cancelled);

        if (itemId.HasValue)
        {
            purchaseQuery = purchaseQuery.Where(l => l.ItemId == itemId.Value);
            salesQuery = salesQuery.Where(l => l.ItemId == itemId.Value);
        }

        var purchaseStacks = await purchaseQuery
            .Select(l => l.StackNo != null && l.StackNo != "" ? l.StackNo : l.Item!.StackNo)
            .Where(s => s != null && s != "")
            .Distinct()
            .ToListAsync(cancellationToken);

        var salesStacks = await salesQuery
            .Select(l => l.StackNo != null && l.StackNo != "" ? l.StackNo : l.Item.StackNo)
            .Where(s => s != null && s != "")
            .Distinct()
            .ToListAsync(cancellationToken);

        var purchaseLots = await purchaseQuery
            .Select(l => l.LotNo != null && l.LotNo != "" ? l.LotNo : l.Item!.LotNo)
            .Where(s => s != null && s != "")
            .Distinct()
            .ToListAsync(cancellationToken);

        var salesLots = await salesQuery
            .Select(l => l.LotNo != null && l.LotNo != "" ? l.LotNo : l.Item.LotNo)
            .Where(s => s != null && s != "")
            .Distinct()
            .ToListAsync(cancellationToken);

        return new StackLotFilterLookupDto(
            purchaseStacks.Concat(salesStacks).Distinct().OrderBy(s => s).ToList(),
            purchaseLots.Concat(salesLots).Distinct().OrderBy(l => l).ToList());
    }

    private static string? ResolveStackLot(string? lineValue, string? itemValue)
    {
        if (!string.IsNullOrWhiteSpace(lineValue))
        {
            return lineValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(itemValue))
        {
            return itemValue.Trim();
        }

        return null;
    }

    private static StackLotPurchaseRow NormalizePurchaseRow(StackLotPurchaseRow row) =>
        row with
        {
            StackNo = ResolveStackLot(row.StackNo, row.ItemStackNo),
            LotNo = ResolveStackLot(row.LotNo, row.ItemLotNo)
        };

    private static StackLotSaleRow NormalizeSaleRow(StackLotSaleRow row) =>
        row with
        {
            StackNo = ResolveStackLot(row.StackNo, row.ItemStackNo),
            LotNo = ResolveStackLot(row.LotNo, row.ItemLotNo)
        };

    private static decimal SignedMultiplier(InvoiceType invoiceType) =>
        invoiceType == InvoiceType.CreditNote ? -1m : 1m;

    private static decimal SignedCartons(StackLotSaleRow sale) =>
        Math.Round(sale.Cartons * SignedMultiplier(sale.InvoiceType), 2);

    private static decimal SignedWeight(StackLotSaleRow sale) =>
        Math.Round(sale.Quantity * SignedMultiplier(sale.InvoiceType), 2);

    private static decimal SignedAmount(StackLotSaleRow sale) =>
        Math.Round(sale.LineTotal * SignedMultiplier(sale.InvoiceType), 2);

    private static string SaleMovementLabel(StackLotSaleRow sale)
    {
        if (sale.InvoiceType == InvoiceType.CreditNote)
        {
            return "Credit Note";
        }

        return sale.Status == InvoiceStatus.Draft ? "Sale (Draft)" : "Sale";
    }

    private sealed record StackLotKey(int ItemId, string? StackNo, string? LotNo)
    {
        public static StackLotKey From(int itemId, string? stackNo, string? lotNo) =>
            new(itemId, NormalizeKeyPart(stackNo), NormalizeKeyPart(lotNo));

        public bool Matches(int itemId, string? stackNo, string? lotNo) =>
            From(itemId, stackNo, lotNo).Equals(this);

        private static string? NormalizeKeyPart(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private sealed record StackLotPurchaseRow(
        int ItemId,
        string ItemCode,
        string ItemName,
        string? StackNo,
        string? LotNo,
        string ItemStackNo,
        string ItemLotNo,
        decimal Quantity,
        decimal Cartons,
        decimal Amount,
        string BillNumber,
        DateTime BillDate);

    private sealed record StackLotSaleRow(
        int ItemId,
        string ItemCode,
        string ItemName,
        string? StackNo,
        string? LotNo,
        string ItemStackNo,
        string ItemLotNo,
        decimal Quantity,
        decimal Cartons,
        decimal LineTotal,
        string InvoiceNumber,
        DateTime InvoiceDate,
        InvoiceType InvoiceType,
        InvoiceStatus Status);

    private (int CompanyId, DateTime From, DateTime To) ValidateDateRange(PurchaseReportRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
        {
            throw new InvalidOperationException("From and to dates are required.");
        }

        if (request.FromDate.Date > request.ToDate.Date)
        {
            throw new InvalidOperationException("From date cannot be after to date.");
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var from = request.FromDate.Date;
        var to = request.ToDate.Date.AddDays(1).AddTicks(-1);
        return (companyId, from, to);
    }

    private IQueryable<VendorBill> BuildBillQuery(
        int companyId,
        DateTime from,
        DateTime to,
        PurchaseReportRequest request)
    {
        var query = _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.CompanyId == companyId
                        && b.BillDate >= from
                        && b.BillDate <= to);

        if (request.ApprovedOnly)
        {
            query = query.Where(b => b.Status == BillStatus.Approved);
        }
        else
        {
            query = query.Where(b => b.Status != BillStatus.Cancelled);
        }

        return query;
    }
}
