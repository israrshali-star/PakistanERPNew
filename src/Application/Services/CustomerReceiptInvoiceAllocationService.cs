using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class CustomerReceiptInvoiceAllocationService : ICustomerReceiptInvoiceAllocationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public CustomerReceiptInvoiceAllocationService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<CustomerReceiptInvoiceAllocationDto?> GetAllocationAsync(
        int customerId,
        DateTime receiptDate,
        decimal amount,
        int? receiptId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var receiptDateOnly = receiptDate.Date;

        var customer = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == customerId && c.CompanyId == companyId)
            .Select(c => new { c.Id, c.OpeningBalance })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si =>
                si.CustomerId == customerId
                && si.CompanyId == companyId
                && si.Status == InvoiceStatus.Posted)
            .Select(si => new
            {
                si.Id,
                si.InvoiceNumber,
                si.InvoiceDate,
                si.InvoiceType,
                si.NetTotal
            })
            .ToListAsync(cancellationToken);

        var receipts = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CustomerId == customerId && r.CompanyId == companyId)
            .Select(r => new
            {
                r.Id,
                r.ReceiptDate,
                r.Amount,
                r.PaymentMethod,
                r.Status,
                r.ClearedAt
            })
            .ToListAsync(cancellationToken);

        var writeCheques = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CustomerId == customerId
                && bt.CompanyId == companyId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.JournalEntryId != null)
            .Select(bt => new
            {
                bt.Id,
                bt.TransactionDate,
                bt.ChequeNumber,
                bt.CustomerBalanceEffect
            })
            .ToListAsync(cancellationToken);

        var invoiceNet = invoices.Sum(i =>
            i.InvoiceType == InvoiceType.CreditNote ? -i.NetTotal : i.NetTotal);
        var otherReceipts = receipts
            .Where(r => r.Id != receiptId
                        && CustomerReceiptBalanceRules.AffectsCustomerBalance(
                            r.PaymentMethod,
                            r.Status,
                            r.ClearedAt))
            .Sum(r => r.Amount);
        var writeChequeEffect = writeCheques.Sum(bt => bt.CustomerBalanceEffect);
        var outstandingBefore = customer.OpeningBalance + invoiceNet - otherReceipts + writeChequeEffect;

        var movements = new List<CustomerReceiptInvoiceAllocator.Movement>(
            invoices.Count + receipts.Count + writeCheques.Count + 1);

        foreach (var invoice in invoices)
        {
            var isCreditNote = invoice.InvoiceType == InvoiceType.CreditNote;
            movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                invoice.InvoiceDate.Date,
                invoice.Id,
                IsReceivable: !isCreditNote,
                invoice.InvoiceNumber,
                invoice.NetTotal));
        }

        foreach (var receipt in receipts)
        {
            if (receipt.Id == receiptId)
            {
                continue;
            }

            if (!CustomerReceiptBalanceRules.AffectsCustomerBalance(
                    receipt.PaymentMethod,
                    receipt.Status,
                    receipt.ClearedAt))
            {
                continue;
            }

            movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                receipt.ReceiptDate.Date,
                CustomerReceiptInvoiceAllocator.ReceiptSortOffset + receipt.Id,
                IsReceivable: false,
                receipt.Id.ToString(),
                receipt.Amount));
        }

        foreach (var cheque in writeCheques)
        {
            if (cheque.CustomerBalanceEffect == 0m)
            {
                continue;
            }

            var reference = !string.IsNullOrWhiteSpace(cheque.ChequeNumber)
                ? cheque.ChequeNumber.Trim()
                : $"PAY-{cheque.Id:D4}";
            movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                cheque.TransactionDate.Date,
                CustomerReceiptInvoiceAllocator.WriteChequeSortOffset + cheque.Id,
                IsReceivable: cheque.CustomerBalanceEffect > 0m,
                reference,
                Math.Abs(cheque.CustomerBalanceEffect)));
        }

        var targetSortKey = receiptId.HasValue
            ? CustomerReceiptInvoiceAllocator.ReceiptSortOffset + receiptId.Value
            : int.MaxValue;
        movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
            receiptDateOnly,
            targetSortKey,
            IsReceivable: false,
            "Receipt",
            Math.Max(0m, amount),
            IsTargetReceipt: true));

        return CustomerReceiptInvoiceAllocator.Allocate(
            customer.OpeningBalance,
            movements,
            Math.Max(0m, amount),
            outstandingBefore);
    }
}
