using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class StackLotInventoryService : IStackLotInventoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public StackLotInventoryService(IUnitOfWork unitOfWork, ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<StackLotAvailabilityDto?> GetAvailabilityAsync(
        int itemId,
        string? stackNo,
        string? lotNo,
        int? excludeInvoiceId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var item = await _unitOfWork.Repository<Domain.Entities.Item>()
            .Query()
            .Where(i => i.Id == itemId && i.CompanyId == companyId)
            .Select(i => new { i.Id, i.ItemCode, i.StackNo, i.LotNo })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return null;
        }

        var resolvedStack = ResolveStackLot(stackNo, item.StackNo);
        var resolvedLot = ResolveStackLot(lotNo, item.LotNo);
        var key = StackLotKey.From(itemId, resolvedStack, resolvedLot);
        var balances = await BuildBalanceMapAsync(companyId, [itemId], excludeInvoiceId, cancellationToken);

        if (!balances.TryGetValue(key, out var balance))
        {
            return new StackLotAvailabilityDto(
                itemId,
                item.ItemCode,
                resolvedStack,
                resolvedLot,
                false,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }

        return ToAvailabilityDto(balance);
    }

    public async Task<(bool Success, string? Message)> ValidateSaleLinesAsync(
        InvoiceType invoiceType,
        IReadOnlyList<StackLotSaleValidationLine> lines,
        int? excludeInvoiceId = null,
        CancellationToken cancellationToken = default)
    {
        if (invoiceType == InvoiceType.CreditNote || lines.Count == 0)
        {
            return (true, null);
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var balances = await BuildBalanceMapAsync(companyId, itemIds, excludeInvoiceId, cancellationToken);

        var grouped = lines
            .Select(l => new
            {
                Key = StackLotKey.From(l.ItemId, l.StackNo, l.LotNo),
                l.ItemCode,
                l.Quantity,
                l.Cartons
            })
            .GroupBy(x => x.Key);

        foreach (var group in grouped)
        {
            var key = group.Key;
            var itemCode = group.First().ItemCode;
            var requestedWeight = group.Sum(x => x.Quantity);
            var requestedCartons = group.Sum(x => x.Cartons);

            if (string.IsNullOrWhiteSpace(key.StackNo))
            {
                return (false, $"Stack number is required for item {itemCode}.");
            }

            if (!balances.TryGetValue(key, out var balance) || !balance.Exists)
            {
                var lotPart = string.IsNullOrWhiteSpace(key.LotNo) ? string.Empty : $" / lot {key.LotNo}";
                return (false,
                    $"Stack {key.StackNo}{lotPart} does not exist for item {itemCode}. No approved purchase was found for this stack.");
            }

            if (requestedWeight > balance.RemainingWeight)
            {
                var lotPart = string.IsNullOrWhiteSpace(key.LotNo) ? string.Empty : $" / lot {key.LotNo}";
                return (false,
                    $"Insufficient weight on stack {key.StackNo}{lotPart} for {itemCode}: " +
                    $"requested {requestedWeight:N2}, available {balance.RemainingWeight:N2} " +
                    $"(purchased {balance.PurchasedWeight:N2}, already sold {balance.SoldWeight:N2}).");
            }

            if (balance.PurchasedCartons > 0m && requestedCartons > balance.RemainingCartons)
            {
                var lotPart = string.IsNullOrWhiteSpace(key.LotNo) ? string.Empty : $" / lot {key.LotNo}";
                return (false,
                    $"Insufficient cartons on stack {key.StackNo}{lotPart} for {itemCode}: " +
                    $"requested {requestedCartons:N2}, available {balance.RemainingCartons:N2} " +
                    $"(purchased {balance.PurchasedCartons:N2}, already sold {balance.SoldCartons:N2}).");
            }
        }

        return (true, null);
    }

    private async Task<Dictionary<StackLotKey, StackLotBalance>> BuildBalanceMapAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        int? excludeInvoiceId,
        CancellationToken cancellationToken)
    {
        var purchaseQuery = _unitOfWork.Repository<Domain.Entities.VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId != null);

        var salesQuery = _unitOfWork.Repository<Domain.Entities.SalesInvoiceLine>()
            .Query()
            .Where(l => l.SalesInvoice.CompanyId == companyId
                        && l.SalesInvoice.Status != InvoiceStatus.Cancelled);

        if (itemIds.Count > 0)
        {
            purchaseQuery = purchaseQuery.Where(l => itemIds.Contains(l.ItemId!.Value));
            salesQuery = salesQuery.Where(l => itemIds.Contains(l.ItemId));
        }

        if (excludeInvoiceId.HasValue)
        {
            salesQuery = salesQuery.Where(l => l.SalesInvoiceId != excludeInvoiceId.Value);
        }

        var purchases = (await purchaseQuery
            .Select(l => new PurchaseRow(
                l.ItemId!.Value,
                l.Item!.ItemCode,
                l.StackNo,
                l.LotNo,
                l.Item.StackNo,
                l.Item.LotNo,
                l.Quantity,
                l.Cartons))
            .ToListAsync(cancellationToken))
            .Select(NormalizePurchase)
            .ToList();

        var sales = (await salesQuery
            .Select(l => new SaleRow(
                l.ItemId,
                l.Item.ItemCode,
                l.StackNo,
                l.LotNo,
                l.Item.StackNo,
                l.Item.LotNo,
                l.Quantity,
                l.Cartons,
                l.SalesInvoice.InvoiceType))
            .ToListAsync(cancellationToken))
            .Select(NormalizeSale)
            .ToList();

        var balances = new Dictionary<StackLotKey, StackLotBalance>();

        foreach (var purchase in purchases)
        {
            var key = StackLotKey.From(purchase.ItemId, purchase.StackNo, purchase.LotNo);
            if (!balances.TryGetValue(key, out var balance))
            {
                balance = new StackLotBalance(purchase.ItemId, purchase.ItemCode, key.StackNo, key.LotNo);
                balances[key] = balance;
            }

            balance.PurchasedWeight += purchase.Quantity;
            balance.PurchasedCartons += purchase.Cartons;
        }

        foreach (var sale in sales)
        {
            var key = StackLotKey.From(sale.ItemId, sale.StackNo, sale.LotNo);
            if (!balances.TryGetValue(key, out var balance))
            {
                balance = new StackLotBalance(sale.ItemId, sale.ItemCode, key.StackNo, key.LotNo);
                balances[key] = balance;
            }

            var multiplier = sale.InvoiceType == InvoiceType.CreditNote ? -1m : 1m;
            balance.SoldWeight += sale.Quantity * multiplier;
            balance.SoldCartons += sale.Cartons * multiplier;
        }

        foreach (var balance in balances.Values)
        {
            balance.RemainingWeight = Math.Round(balance.PurchasedWeight - balance.SoldWeight, 2);
            balance.RemainingCartons = Math.Round(balance.PurchasedCartons - balance.SoldCartons, 2);
        }

        return balances;
    }

    private static StackLotAvailabilityDto ToAvailabilityDto(StackLotBalance balance) =>
        new(
            balance.ItemId,
            balance.ItemCode,
            balance.StackNo,
            balance.LotNo,
            balance.Exists,
            balance.PurchasedWeight,
            balance.SoldWeight,
            balance.RemainingWeight,
            balance.PurchasedCartons,
            balance.SoldCartons,
            balance.RemainingCartons);

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

    private static PurchaseRow NormalizePurchase(PurchaseRow row) =>
        row with
        {
            StackNo = ResolveStackLot(row.StackNo, row.ItemStackNo),
            LotNo = ResolveStackLot(row.LotNo, row.ItemLotNo)
        };

    private static SaleRow NormalizeSale(SaleRow row) =>
        row with
        {
            StackNo = ResolveStackLot(row.StackNo, row.ItemStackNo),
            LotNo = ResolveStackLot(row.LotNo, row.ItemLotNo)
        };

    private sealed class StackLotBalance
    {
        public StackLotBalance(int itemId, string itemCode, string? stackNo, string? lotNo)
        {
            ItemId = itemId;
            ItemCode = itemCode;
            StackNo = stackNo;
            LotNo = lotNo;
        }

        public int ItemId { get; }
        public string ItemCode { get; }
        public string? StackNo { get; }
        public string? LotNo { get; }
        public decimal PurchasedWeight { get; set; }
        public decimal PurchasedCartons { get; set; }
        public decimal SoldWeight { get; set; }
        public decimal SoldCartons { get; set; }
        public decimal RemainingWeight { get; set; }
        public decimal RemainingCartons { get; set; }
        public bool Exists => PurchasedWeight > 0m || PurchasedCartons > 0m;
    }

    private sealed record StackLotKey(int ItemId, string? StackNo, string? LotNo)
    {
        public static StackLotKey From(int itemId, string? stackNo, string? lotNo) =>
            new(itemId, NormalizeKeyPart(stackNo), NormalizeKeyPart(lotNo));

        private static string? NormalizeKeyPart(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private sealed record PurchaseRow(
        int ItemId,
        string ItemCode,
        string? StackNo,
        string? LotNo,
        string ItemStackNo,
        string ItemLotNo,
        decimal Quantity,
        decimal Cartons);

    private sealed record SaleRow(
        int ItemId,
        string ItemCode,
        string? StackNo,
        string? LotNo,
        string ItemStackNo,
        string ItemLotNo,
        decimal Quantity,
        decimal Cartons,
        InvoiceType InvoiceType);
}
