using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class EntitySearchService : IEntitySearchService
{
    private const int MaxLimit = 50;
    private const int DefaultLimit = 20;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly IStackLotInventoryService _stackLotInventory;

    public EntitySearchService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        IStackLotInventoryService stackLotInventory)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _stackLotInventory = stackLotInventory;
    }

    public async Task<EntitySearchResponse> SearchAsync(
        string entity,
        string? query,
        string? id,
        int limit,
        string? itemType,
        CancellationToken cancellationToken = default)
    {
        var take = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
        var term = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
        var key = (entity ?? string.Empty).Trim().ToLowerInvariant();

        var results = key switch
        {
            "customer" or "customers" => await SearchCustomersAsync(term, id, take, cancellationToken),
            "vendor" or "vendors" => await SearchVendorsAsync(term, id, take, cancellationToken),
            "item" or "items" => await SearchItemsAsync(term, id, take, itemType, cancellationToken),
            "account" or "accounts" or "coa" => await SearchAccountsAsync(term, id, take, cancellationToken),
            "bank" or "banks" => await SearchBanksAsync(term, id, take, cancellationToken),
            "warehouse" or "warehouses" => await SearchWarehousesAsync(term, id, take, cancellationToken),
            "lot" or "lots" => await SearchLotsAsync(term, id, take, cancellationToken),
            "stack" or "stacks" => await SearchStacksAsync(term, id, take, cancellationToken),
            "invoice" or "invoices" => await SearchInvoicesAsync(term, id, take, cancellationToken),
            "bill" or "bills" => await SearchBillsAsync(term, id, take, cancellationToken),
            "receipt" or "receipts" => await SearchReceiptsAsync(term, id, take, cancellationToken),
            "journal" or "journals" => await SearchJournalsAsync(term, id, take, cancellationToken),
            "party" or "parties" => await SearchPartiesAsync(term, id, take, cancellationToken),
            _ => throw new InvalidOperationException("Unknown search entity.")
        };

        return new EntitySearchResponse(results);
    }

    private int CompanyId() => _currentCompany.GetRequiredCompanyId();

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchCustomersAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive && !c.IsDeleted);

        if (int.TryParse(id, out var customerId) && customerId > 0)
        {
            query = query.Where(c => c.Id == customerId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(c =>
                c.BuyerId.Contains(term)
                || c.BuyerName.Contains(term)
                || (c.NTN != null && c.NTN.Contains(term))
                || (c.Phone != null && c.Phone.Contains(term))
                || (c.Mobile != null && c.Mobile.Contains(term)));
        }

        return await query
            .OrderBy(c => c.BuyerName)
            .Take(take)
            .Select(c => new EntitySearchItemDto
            {
                Id = c.Id.ToString(),
                Text = c.BuyerId + " — " + c.BuyerName,
                BuyerId = c.BuyerId,
                BuyerName = c.BuyerName,
                ScenarioId = c.ScenarioId,
                ProvinceId = c.ProvinceId,
                Address = c.Address,
                Ntn = c.NTN,
                Cnic = c.CNIC,
                InvoiceType = (int)c.InvoiceType,
                FurtherTaxRate = c.FurtherTaxRate,
                Balance = c.OpeningBalance
                    + c.SalesInvoices
                        .Where(si => si.Status == InvoiceStatus.Posted)
                        .Sum(si => si.InvoiceType == InvoiceType.CreditNote ? -si.NetTotal : si.NetTotal)
                    - c.CustomerReceipts
                        .Where(r => r.PaymentMethod != PaymentMethod.Cheque
                                    || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null))
                        .Sum(r => r.Amount)
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchVendorsAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive && !v.IsDeleted);

        if (int.TryParse(id, out var vendorId) && vendorId > 0)
        {
            query = query.Where(v => v.Id == vendorId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(v =>
                v.VendorCode.Contains(term)
                || v.VendorName.Contains(term)
                || (v.NTN != null && v.NTN.Contains(term))
                || (v.Phone != null && v.Phone.Contains(term)));
        }

        return await query
            .OrderBy(v => v.VendorName)
            .Take(take)
            .Select(v => new EntitySearchItemDto
            {
                Id = v.Id.ToString(),
                Text = v.VendorCode + " — " + v.VendorName,
                VendorCode = v.VendorCode,
                VendorName = v.VendorName,
                Address = v.Address,
                Ntn = v.NTN,
                DefaultTaxRate = v.DefaultSalesTaxRate,
                Balance = v.OpeningBalance
                    + v.VendorBills.Where(b => b.Status == BillStatus.Approved).Sum(b => b.NetAmount)
                    - v.VendorPayments.Sum(p => p.Amount)
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchItemsAsync(
        string term,
        string? id,
        int take,
        string? itemType,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive && !i.IsDeleted);

        if (string.Equals(itemType, "goods", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.ItemType == ItemType.Goods);
        }

        if (int.TryParse(id, out var itemId) && itemId > 0)
        {
            query = query.Where(i => i.Id == itemId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(i =>
                i.ItemCode.Contains(term)
                || i.ItemName.Contains(term)
                || i.LotNo.Contains(term)
                || (i.Barcode != null && i.Barcode.Contains(term)));
        }

        return await query
            .OrderBy(i => i.ItemName)
            .Take(take)
            .Select(i => new EntitySearchItemDto
            {
                Id = i.Id.ToString(),
                Text = i.ItemCode + " — " + i.ItemName,
                ItemCode = i.ItemCode,
                ItemName = i.ItemName,
                LotNo = i.LotNo,
                UnitSymbol = i.UnitOfMeasure.Symbol ?? i.UnitOfMeasure.Name,
                CurrentStock = i.CurrentStock
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchAccountsAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive && !a.IsDeleted);

        if (int.TryParse(id, out var accountId) && accountId > 0)
        {
            query = query.Where(a => a.Id == accountId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(a =>
                a.AccountNumber.Contains(term)
                || a.AccountName.Contains(term));
        }

        return await query
            .OrderBy(a => a.AccountNumber)
            .Take(take)
            .Select(a => new EntitySearchItemDto
            {
                Id = a.Id.ToString(),
                Text = a.AccountNumber + " — " + a.AccountName,
                AccountNumber = a.AccountNumber,
                AccountName = a.AccountName
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchBanksAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<Bank>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.IsActive && !b.IsDeleted);

        if (int.TryParse(id, out var bankId) && bankId > 0)
        {
            query = query.Where(b => b.Id == bankId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(b =>
                b.BankName.Contains(term)
                || b.AccountNumber.Contains(term)
                || b.AccountTitle.Contains(term));
        }

        return await query
            .OrderBy(b => b.BankName)
            .Take(take)
            .Select(b => new EntitySearchItemDto
            {
                Id = b.Id.ToString(),
                Text = b.BankName + " (" + b.AccountNumber + ")",
                BankName = b.BankName,
                AccountTitle = b.AccountTitle,
                AccountNumber = b.AccountNumber,
                Balance = b.CurrentBalance
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchWarehousesAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId && w.IsActive && !w.IsDeleted);

        if (int.TryParse(id, out var warehouseId) && warehouseId > 0)
        {
            query = query.Where(w => w.Id == warehouseId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(w => w.Code.Contains(term) || w.Name.Contains(term));
        }

        return await query
            .OrderBy(w => w.Name)
            .Take(take)
            .Select(w => new EntitySearchItemDto
            {
                Id = w.Id.ToString(),
                Text = w.Code + " — " + w.Name,
                Code = w.Code,
                Name = w.Name
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchLotsAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LotItemOptionDto> lots;
        if (!string.IsNullOrWhiteSpace(id))
        {
            var sep = id.IndexOf('|');
            var itemCode = sep > 0 ? id[..sep] : null;
            var lotNo = sep > 0 ? id[(sep + 1)..] : id;
            lots = await _stackLotInventory.SearchLotNumbersAsync(itemCode ?? lotNo, take, cancellationToken);
            lots = lots
                .Where(x => string.Equals(x.ItemCode + "|" + x.LotNo, id, StringComparison.OrdinalIgnoreCase)
                            || (itemCode is null && string.Equals(x.LotNo, lotNo, StringComparison.OrdinalIgnoreCase)))
                .Take(1)
                .ToList();
        }
        else
        {
            lots = await _stackLotInventory.SearchLotNumbersAsync(term, take, cancellationToken);
        }

        return lots
            .Select(x => new EntitySearchItemDto
            {
                Id = string.IsNullOrWhiteSpace(x.ItemCode) ? x.LotNo : x.ItemCode + "|" + x.LotNo,
                Text = string.IsNullOrWhiteSpace(x.LotNo) ? x.ItemCode + " + —" : x.ItemCode + " + " + x.LotNo,
                ItemCode = x.ItemCode,
                LotNo = x.LotNo
            })
            .ToList();
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchStacksAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StackItemOptionDto> stacks;
        if (!string.IsNullOrWhiteSpace(id))
        {
            stacks = await _stackLotInventory.SearchStackNumbersAsync(id, take, cancellationToken);
            stacks = stacks
                .Where(x => string.Equals(x.StackNo, id, StringComparison.OrdinalIgnoreCase))
                .Take(1)
                .ToList();
        }
        else
        {
            stacks = await _stackLotInventory.SearchStackNumbersAsync(term, take, cancellationToken);
        }

        return stacks
            .Select(x => new EntitySearchItemDto
            {
                Id = x.StackNo,
                Text = string.IsNullOrWhiteSpace(x.ItemCode)
                    ? x.StackNo
                    : x.StackNo + " — " + x.ItemCode + " " + x.ItemName,
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                StackNo = x.StackNo
            })
            .ToList();
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchInvoicesAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId && !i.IsDeleted);

        if (int.TryParse(id, out var invoiceId) && invoiceId > 0)
        {
            query = query.Where(i => i.Id == invoiceId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(i =>
                i.InvoiceNumber.Contains(term)
                || (i.FbrInvoiceNumber != null && i.FbrInvoiceNumber.Contains(term))
                || i.Customer.BuyerName.Contains(term));
        }

        return await query
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .Take(take)
            .Select(i => new EntitySearchItemDto
            {
                Id = i.Id.ToString(),
                Text = i.InvoiceNumber + " — " + i.Customer.BuyerName,
                Code = i.InvoiceNumber,
                Name = i.Customer.BuyerName,
                BuyerName = i.Customer.BuyerName
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchBillsAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.CompanyId == companyId && !b.IsDeleted);

        if (int.TryParse(id, out var billId) && billId > 0)
        {
            query = query.Where(b => b.Id == billId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(b =>
                b.BillNumber.Contains(term)
                || (b.RefNo != null && b.RefNo.Contains(term))
                || b.Vendor.VendorName.Contains(term));
        }

        return await query
            .OrderByDescending(b => b.BillDate)
            .ThenByDescending(b => b.Id)
            .Take(take)
            .Select(b => new EntitySearchItemDto
            {
                Id = b.Id.ToString(),
                Text = b.BillNumber + (b.RefNo != null && b.RefNo != "" ? " / " + b.RefNo : "") + " — " + b.Vendor.VendorName,
                Code = b.BillNumber,
                Name = b.RefNo,
                VendorName = b.Vendor.VendorName
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchReceiptsAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CompanyId == companyId && !r.IsDeleted);

        if (int.TryParse(id, out var receiptId) && receiptId > 0)
        {
            query = query.Where(r => r.Id == receiptId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(r =>
                r.ReceiptNumber.Contains(term)
                || r.Customer.BuyerName.Contains(term));
        }

        return await query
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .Select(r => new EntitySearchItemDto
            {
                Id = r.Id.ToString(),
                Text = r.ReceiptNumber + " — " + r.Customer.BuyerName,
                Code = r.ReceiptNumber,
                BuyerName = r.Customer.BuyerName
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchJournalsAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var query = _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.CompanyId == companyId && !j.IsDeleted);

        if (int.TryParse(id, out var entryId) && entryId > 0)
        {
            query = query.Where(j => j.Id == entryId);
        }
        else if (term.Length > 0)
        {
            query = query.Where(j =>
                j.EntryNumber.Contains(term)
                || (j.Description != null && j.Description.Contains(term)));
        }

        return await query
            .OrderByDescending(j => j.EntryDate)
            .ThenByDescending(j => j.Id)
            .Take(take)
            .Select(j => new EntitySearchItemDto
            {
                Id = j.Id.ToString(),
                Text = j.EntryNumber + (j.Description != null && j.Description != "" ? " — " + j.Description : ""),
                Code = j.EntryNumber,
                Name = j.Description
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntitySearchItemDto>> SearchPartiesAsync(
        string term,
        string? id,
        int take,
        CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var results = new List<EntitySearchItemDto>();

        int? parsedCustomerId = null;
        int? parsedVendorId = null;
        int? parsedCoaId = null;
        if (!string.IsNullOrWhiteSpace(id))
        {
            var parts = id.Split(':');
            if (parts.Length >= 3)
            {
                if (int.TryParse(parts[0], out var cid) && cid > 0) parsedCustomerId = cid;
                if (int.TryParse(parts[1], out var vid) && vid > 0) parsedVendorId = vid;
                if (int.TryParse(parts[2], out var aid) && aid > 0) parsedCoaId = aid;
            }
        }

        var arAccount = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive && !a.IsDeleted
                        && a.AccountNumber == GlAccountNumbers.AccountsReceivable)
            .Select(a => new { a.Id, a.AccountNumber })
            .FirstOrDefaultAsync(cancellationToken);

        var apAccount = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive && !a.IsDeleted
                        && a.AccountNumber == GlAccountNumbers.AccountsPayable)
            .Select(a => new { a.Id, a.AccountNumber })
            .FirstOrDefaultAsync(cancellationToken);

        var customerQuery = _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive && !c.IsDeleted);

        if (parsedCustomerId.HasValue)
        {
            customerQuery = customerQuery.Where(c => c.Id == parsedCustomerId.Value);
        }
        else if (term.Length > 0)
        {
            customerQuery = customerQuery.Where(c =>
                c.BuyerId.Contains(term) || c.BuyerName.Contains(term));
        }

        if (arAccount is not null && (parsedCustomerId.HasValue || parsedVendorId is null))
        {
            var customers = await customerQuery
                .OrderBy(c => c.BuyerName)
                .Take(take)
                .Select(c => new
                {
                    c.Id,
                    c.BuyerId,
                    c.BuyerName,
                    Balance = c.OpeningBalance
                        + c.SalesInvoices.Where(si => si.Status == InvoiceStatus.Posted)
                            .Sum(si => si.InvoiceType == InvoiceType.CreditNote ? -si.NetTotal : si.NetTotal)
                        - c.CustomerReceipts
                            .Where(r => r.PaymentMethod != PaymentMethod.Cheque
                                        || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null))
                            .Sum(r => r.Amount)
                        + c.WriteChequePayments
                            .Where(bt => bt.TransactionType == BankTransactionType.Withdrawal && !bt.IsDeleted)
                            .Sum(bt => bt.CustomerBalanceEffect)
                })
                .ToListAsync(cancellationToken);

            foreach (var customer in customers)
            {
                results.Add(new EntitySearchItemDto
                {
                    Id = customer.Id + ":0:" + arAccount.Id,
                    Text = "[AR] " + customer.BuyerName + " (" + customer.BuyerId + ") — " + arAccount.AccountNumber
                           + " (PKR " + customer.Balance.ToString("N2") + ")",
                    Group = "Accounts Receivable (Customers)",
                    CustomerId = customer.Id,
                    ChartOfAccountId = arAccount.Id,
                    PartyType = "AR",
                    PartyName = customer.BuyerName,
                    PartyCode = customer.BuyerId,
                    BuyerName = customer.BuyerName,
                    Balance = customer.Balance
                });
            }
        }

        var vendorQuery = _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive && !v.IsDeleted);

        if (parsedVendorId.HasValue)
        {
            vendorQuery = vendorQuery.Where(v => v.Id == parsedVendorId.Value);
        }
        else if (term.Length > 0)
        {
            vendorQuery = vendorQuery.Where(v =>
                v.VendorCode.Contains(term) || v.VendorName.Contains(term));
        }

        if (apAccount is not null && (parsedVendorId.HasValue || parsedCustomerId is null))
        {
            var vendors = await vendorQuery
                .OrderBy(v => v.VendorName)
                .Take(take)
                .Select(v => new
                {
                    v.Id,
                    v.VendorCode,
                    v.VendorName,
                    Balance = v.OpeningBalance
                        + v.VendorBills.Where(b => b.Status == BillStatus.Approved).Sum(b => b.NetAmount)
                        - v.VendorPayments.Sum(p => p.Amount)
                        - v.WriteChequePayments
                            .Where(bt => bt.TransactionType == BankTransactionType.Withdrawal && !bt.IsDeleted)
                            .Sum(bt => bt.Amount)
                })
                .ToListAsync(cancellationToken);

            foreach (var vendor in vendors)
            {
                results.Add(new EntitySearchItemDto
                {
                    Id = "0:" + vendor.Id + ":" + apAccount.Id,
                    Text = "[AP] " + vendor.VendorName + " (" + vendor.VendorCode + ") — " + apAccount.AccountNumber
                           + " (PKR " + vendor.Balance.ToString("N2") + ")",
                    Group = "Accounts Payable (Vendors)",
                    VendorId = vendor.Id,
                    ChartOfAccountId = apAccount.Id,
                    PartyType = "AP",
                    PartyName = vendor.VendorName,
                    PartyCode = vendor.VendorCode,
                    VendorName = vendor.VendorName,
                    Balance = vendor.Balance
                });
            }
        }

        if (parsedCustomerId is null && parsedVendorId is null)
        {
            var accountQuery = _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.CompanyId == companyId && a.IsActive && !a.IsDeleted
                            && !a.ChildAccounts.Any());

            if (parsedCoaId.HasValue)
            {
                accountQuery = accountQuery.Where(a => a.Id == parsedCoaId.Value);
            }
            else if (term.Length > 0)
            {
                accountQuery = accountQuery.Where(a =>
                    a.AccountNumber.Contains(term) || a.AccountName.Contains(term));
            }

            var accounts = await accountQuery
                .OrderBy(a => a.AccountNumber)
                .Take(take)
                .Select(a => new
                {
                    a.Id,
                    a.AccountNumber,
                    a.AccountName,
                    a.OpeningBalance,
                    a.TypeId
                })
                .ToListAsync(cancellationToken);

            var balances = await GetGlBalancesAsync(
                companyId,
                accounts.Select(a => a.Id).ToList(),
                cancellationToken);

            foreach (var account in accounts)
            {
                var isCash = account.AccountNumber == GlAccountNumbers.CashInHand;
                balances.TryGetValue(account.Id, out var totals);
                var rawBalance = GlAccountBalance.ComputeNet(
                    account.OpeningBalance,
                    totals.Debits,
                    totals.Credits,
                    account.TypeId,
                    account.AccountNumber,
                    companyId);
                var balance = account.TypeId == 2
                    ? SalesTaxPaymentGlHelper.LiabilityOutstanding(rawBalance)
                    : rawBalance;
                results.Add(new EntitySearchItemDto
                {
                    Id = "0:0:" + account.Id,
                    Text = (isCash ? "[CASH] " : "[COA] ") + account.AccountName + " — " + account.AccountNumber
                           + " (PKR " + balance.ToString("N2") + ")",
                    Group = isCash ? "Cash in Hand" : "Other Chart of Accounts",
                    ChartOfAccountId = account.Id,
                    PartyType = isCash ? "CASH" : "COA",
                    PartyName = account.AccountName,
                    AccountNumber = account.AccountNumber,
                    AccountName = account.AccountName,
                    Balance = balance
                });
            }
        }

        return results;
    }

    private async Task<Dictionary<int, (decimal Debits, decimal Credits)>> GetGlBalancesAsync(
        int companyId,
        IReadOnlyList<int> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<int, (decimal Debits, decimal Credits)>();
        }

        var rows = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => accountIds.Contains(l.ChartOfAccountId)
                        && l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted)
            .GroupBy(l => l.ChartOfAccountId)
            .Select(g => new
            {
                Id = g.Key,
                Debits = g.Sum(x => x.Debit),
                Credits = g.Sum(x => x.Credit)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => (x.Debits, x.Credits));
    }
}
