using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
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

    public async Task<IReadOnlyList<LotItemOptionDto>> GetLotNumbersAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var fromItems = await _unitOfWork.Repository<Domain.Entities.Item>()
            .Query()
            .Where(i => i.CompanyId == companyId
                        && i.IsActive
                        && i.LotNo != ""
                        && i.ItemType != ItemType.Service)
            .Select(i => new { i.ItemCode, i.LotNo })
            .ToListAsync(cancellationToken);

        var fromServices = await _unitOfWork.Repository<Domain.Entities.Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive && i.ItemType == ItemType.Service)
            .Select(i => new { i.ItemCode, LotNo = string.Empty })
            .ToListAsync(cancellationToken);

        var fromPurchases = await _unitOfWork.Repository<Domain.Entities.VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId != null
                        && l.LotNo != null
                        && l.LotNo != "")
            .Select(l => new { ItemCode = l.Item!.ItemCode, LotNo = l.LotNo! })
            .ToListAsync(cancellationToken);

        return fromItems
            .Concat(fromServices)
            .Concat(fromPurchases)
            .Select(x => new LotItemOptionDto(x.ItemCode.Trim(), x.LotNo.Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
            .GroupBy(x => (ItemCode: x.ItemCode.ToUpperInvariant(), LotNo: x.LotNo.ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LotNo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<LotDetailLookupDto?> GetLotDetailAsync(
        string lotNo,
        string? itemCode = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedLot = string.IsNullOrWhiteSpace(lotNo) ? string.Empty : lotNo.Trim();
        var lotUpper = normalizedLot.ToUpperInvariant();
        var normalizedItemCode = string.IsNullOrWhiteSpace(itemCode) ? null : itemCode.Trim();
        var itemCodeUpper = normalizedItemCode?.ToUpperInvariant();
        var companyId = _currentCompany.GetRequiredCompanyId();

        var itemByCode = itemCodeUpper is null
            ? null
            : await _unitOfWork.Repository<Domain.Entities.Item>()
                .Query()
                .Where(i => i.CompanyId == companyId
                            && i.IsActive
                            && i.ItemCode.ToUpper() == itemCodeUpper)
                .Select(i => new
                {
                    i.Id,
                    i.ItemCode,
                    i.ItemName,
                    i.Description,
                    i.HSCode,
                    i.ItemType,
                    UnitSymbol = i.UnitOfMeasure.Symbol,
                    i.SaleRate,
                    i.PurchaseRate
                })
                .FirstOrDefaultAsync(cancellationToken);

        if (itemByCode is not null
            && (itemByCode.ItemType == ItemType.Service || string.IsNullOrWhiteSpace(normalizedLot)))
        {
            return new LotDetailLookupDto(
                itemByCode.ItemType == ItemType.Service ? string.Empty : normalizedLot,
                itemByCode.Id,
                itemByCode.ItemCode,
                itemByCode.ItemName,
                itemByCode.Description ?? itemByCode.ItemName,
                itemByCode.HSCode,
                InventoryUnitDisplay.Format(itemByCode.ItemCode, itemByCode.UnitSymbol),
                itemByCode.SaleRate,
                itemByCode.PurchaseRate,
                null,
                Array.Empty<string>(),
                itemByCode.ItemType);
        }

        if (string.IsNullOrWhiteSpace(normalizedLot))
        {
            return null;
        }

        var purchaseLine = await _unitOfWork.Repository<Domain.Entities.VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId != null
                        && (itemCodeUpper == null || l.Item!.ItemCode.ToUpper() == itemCodeUpper)
                        && ((l.LotNo != null && l.LotNo.ToUpper() == lotUpper)
                            || (l.LotNo == null && l.Item!.LotNo.ToUpper() == lotUpper)
                            || l.Item!.LotNo.ToUpper() == lotUpper))
            .OrderByDescending(l => l.VendorBill.BillDate)
            .ThenByDescending(l => l.Id)
            .Select(l => new
            {
                l.ItemId,
                l.Description,
                l.StackNo,
                ItemCode = l.Item!.ItemCode,
                ItemName = l.Item.ItemName,
                ItemDescription = l.Item.Description,
                ItemHsCode = l.Item.HSCode,
                ItemStackNo = l.Item.StackNo,
                ItemLotNo = l.Item.LotNo,
                ItemType = l.Item.ItemType,
                UnitSymbol = l.Item.UnitOfMeasure.Symbol,
                l.Item.SaleRate,
                l.Rate,
                l.Item.PurchaseRate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (purchaseLine is not null)
        {
            var itemId = purchaseLine.ItemId!.Value;
            var stackNos = await GetStackNumbersForLotAsync(companyId, itemId, normalizedLot, cancellationToken);
            var defaultStack = ResolveStackLot(purchaseLine.StackNo, purchaseLine.ItemStackNo)
                               ?? stackNos.FirstOrDefault();

            return new LotDetailLookupDto(
                normalizedLot,
                itemId,
                purchaseLine.ItemCode,
                purchaseLine.ItemName,
                purchaseLine.Description ?? purchaseLine.ItemDescription ?? purchaseLine.ItemName,
                purchaseLine.ItemHsCode,
                InventoryUnitDisplay.Format(purchaseLine.ItemCode, purchaseLine.UnitSymbol),
                purchaseLine.SaleRate,
                purchaseLine.Rate > 0m ? purchaseLine.Rate : purchaseLine.PurchaseRate,
                defaultStack,
                stackNos,
                purchaseLine.ItemType);
        }

        var item = await _unitOfWork.Repository<Domain.Entities.Item>()
            .Query()
            .Where(i => i.CompanyId == companyId
                        && i.IsActive
                        && i.LotNo.ToUpper() == lotUpper
                        && (itemCodeUpper == null || i.ItemCode.ToUpper() == itemCodeUpper))
            .Select(i => new
            {
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.Description,
                i.HSCode,
                i.StackNo,
                i.ItemType,
                UnitSymbol = i.UnitOfMeasure.Symbol,
                i.SaleRate,
                i.PurchaseRate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return null;
        }

        var itemStackNos = await GetStackNumbersForLotAsync(companyId, item.Id, normalizedLot, cancellationToken);
        var itemDefaultStack = ResolveStackLot(null, item.StackNo) ?? itemStackNos.FirstOrDefault();

        return new LotDetailLookupDto(
            normalizedLot,
            item.Id,
            item.ItemCode,
            item.ItemName,
            item.Description ?? item.ItemName,
            item.HSCode,
            InventoryUnitDisplay.Format(item.ItemCode, item.UnitSymbol),
            item.SaleRate,
            item.PurchaseRate,
            itemDefaultStack,
            itemStackNos,
            item.ItemType);
    }

    private async Task<IReadOnlyList<string>> GetStackNumbersForLotAsync(
        int companyId,
        int itemId,
        string lotNo,
        CancellationToken cancellationToken)
    {
        var lotUpper = lotNo.ToUpperInvariant();

        var stacks = await _unitOfWork.Repository<Domain.Entities.VendorBillLine>()
            .Query()
            .Where(l => l.VendorBill.CompanyId == companyId
                        && l.VendorBill.Status == BillStatus.Approved
                        && l.ItemId == itemId
                        && ((l.LotNo != null && l.LotNo.ToUpper() == lotUpper)
                            || (l.LotNo == null && l.Item!.LotNo.ToUpper() == lotUpper)
                            || l.Item!.LotNo.ToUpper() == lotUpper))
            .Select(l => l.StackNo ?? l.Item!.StackNo)
            .ToListAsync(cancellationToken);

        return stacks
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
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
        var itemTypes = await _unitOfWork.Repository<Domain.Entities.Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.ItemType })
            .ToDictionaryAsync(i => i.Id, i => i.ItemType, cancellationToken);

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
            if (itemTypes.TryGetValue(key.ItemId, out var itemType) && itemType == ItemType.Service)
            {
                continue;
            }

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
                        && l.SalesInvoice.Status == InvoiceStatus.Posted);

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

        var manualMovementsQuery = _unitOfWork.Repository<Domain.Entities.InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId
                        && t.ReferenceNo != null
                        && !t.ReferenceNo.StartsWith("BILL-")
                        && !t.ReferenceNo.StartsWith("INV-"));

        if (itemIds.Count > 0)
        {
            manualMovementsQuery = manualMovementsQuery.Where(t => itemIds.Contains(t.ItemId));
        }

        var manualMovements = await manualMovementsQuery
            .Select(t => new ManualMovementRow(
                t.ItemId,
                t.Item.ItemCode,
                t.StackNo,
                t.LotNo,
                t.Item.StackNo,
                t.Item.LotNo,
                t.TransactionType,
                t.Quantity))
            .ToListAsync(cancellationToken);

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

        foreach (var movement in manualMovements.Select(NormalizeManualMovement))
        {
            var key = StackLotKey.From(movement.ItemId, movement.StackNo, movement.LotNo);
            if (!balances.TryGetValue(key, out var balance))
            {
                balance = new StackLotBalance(movement.ItemId, movement.ItemCode, key.StackNo, key.LotNo);
                balances[key] = balance;
            }

            switch (movement.TransactionType)
            {
                case InventoryTransactionType.StockIn:
                case InventoryTransactionType.Opening:
                    balance.PurchasedWeight += movement.Quantity;
                    break;
                case InventoryTransactionType.StockOut:
                    balance.SoldWeight += movement.Quantity;
                    break;
            }
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

    private static ManualMovementRow NormalizeManualMovement(ManualMovementRow row) =>
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

    private sealed record ManualMovementRow(
        int ItemId,
        string ItemCode,
        string? StackNo,
        string? LotNo,
        string ItemStackNo,
        string ItemLotNo,
        InventoryTransactionType TransactionType,
        decimal Quantity);
}
