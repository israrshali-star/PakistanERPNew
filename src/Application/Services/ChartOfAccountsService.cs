using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.Json;

namespace PakistanAccountingERP.Application.Services;

public class ChartOfAccountsService : IChartOfAccountsService
{
    private const int LiabilityTypeId = 2;
    private const int EquityTypeId = 3;

    private static readonly Dictionary<int, (int Min, int Max)> TypeNumberRanges = new()
    {
        [1] = (1000, 1999),
        [2] = (2000, 2999),
        [3] = (3000, 3999),
        [4] = (4000, 4999),
        [5] = (5000, 5999),
        [6] = (6000, 6999)
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILedgerPdfService _ledgerPdfService;
    private readonly ILogger<ChartOfAccountsService> _logger;

    public ChartOfAccountsService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILedgerPdfService ledgerPdfService,
        ILogger<ChartOfAccountsService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _ledgerPdfService = ledgerPdfService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChartOfAccountTreeTypeDto>> GetTreeAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var leafBalanceMap = await GetRunningBalanceMapAsync(companyId, cancellationToken);

        var accounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new AccountRow(
                a.Id,
                a.AccountNumber,
                a.AccountName,
                a.TypeId,
                a.SubTypeId,
                a.ParentAccountId,
                a.OpeningBalance,
                a.IsActive))
            .ToListAsync(cancellationToken);

        var childrenLookup = accounts.ToLookup(a => a.ParentAccountId);

        var types = await _unitOfWork.Repository<AccountType>()
            .Query()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeId)
            .Select(t => new { t.TypeId, t.TypeCode, t.TypeName })
            .ToListAsync(cancellationToken);

        var subTypes = await _unitOfWork.Repository<SubAccountType>()
            .Query()
            .OrderBy(s => s.SubTypeCode)
            .Select(s => new { s.SubTypeId, s.TypeId, s.SubTypeCode, s.SubTypeName })
            .ToListAsync(cancellationToken);

        return types.Select(type =>
        {
            var typeSubTypes = subTypes.Where(s => s.TypeId == type.TypeId).ToList();
            var subTypeRoots = typeSubTypes.ToDictionary(
                st => st.SubTypeId,
                st => accounts
                    .Where(a => a.SubTypeId == st.SubTypeId && a.ParentAccountId is null)
                    .Select(a => BuildTreeNode(a, childrenLookup, leafBalanceMap))
                    .OrderBy(a => a.AccountNumber)
                    .ToList());

            var includedIds = new HashSet<int>();
            foreach (var roots in subTypeRoots.Values)
            {
                foreach (var root in roots)
                {
                    CollectTreeAccountIds(root, includedIds);
                }
            }

            var orphans = accounts
                .Where(a => a.TypeId == type.TypeId && !includedIds.Contains(a.Id))
                .OrderBy(a => a.AccountNumber)
                .ToList();

            foreach (var orphan in orphans)
            {
                var subTypeId = orphan.SubTypeId ?? typeSubTypes.FirstOrDefault()?.SubTypeId;
                if (!subTypeId.HasValue)
                {
                    continue;
                }

                if (!subTypeRoots.TryGetValue(subTypeId.Value, out var roots))
                {
                    roots = [];
                    subTypeRoots[subTypeId.Value] = roots;
                }

                var orphanNode = BuildTreeNode(orphan, childrenLookup, leafBalanceMap);
                roots.Add(orphanNode);
                CollectTreeAccountIds(orphanNode, includedIds);
            }

            var subTypeNodes = typeSubTypes
                .Select(subType => new ChartOfAccountTreeSubTypeDto(
                    subType.SubTypeId,
                    subType.SubTypeCode,
                    subType.SubTypeName,
                    subTypeRoots.GetValueOrDefault(subType.SubTypeId) ?? []))
                .ToList();

            return new ChartOfAccountTreeTypeDto(
                type.TypeId,
                type.TypeCode,
                type.TypeName,
                subTypeNodes);
        }).ToList();
    }

    public async Task<IReadOnlyList<ParentAccountLookupDto>> GetParentAccountsAsync(
        int typeId,
        int subTypeId,
        int? excludeAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId
                        && a.TypeId == typeId
                        && a.SubTypeId == subTypeId
                        && a.ParentAccountId == null
                        && (!excludeAccountId.HasValue || a.Id != excludeAccountId.Value))
            .OrderBy(a => a.AccountNumber)
            .Select(a => new ParentAccountLookupDto(a.Id, a.AccountNumber, a.AccountName))
            .ToListAsync(cancellationToken);
    }

    public async Task<ChartOfAccountDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var account = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == id && a.CompanyId == companyId)
            .Select(a => new
            {
                a.Id,
                a.AccountNumber,
                a.AccountName,
                a.TypeId,
                TypeName = a.AccountType != null ? a.AccountType.TypeName : null,
                a.SubTypeId,
                SubTypeName = a.SubAccountType != null ? a.SubAccountType.SubTypeName : null,
                a.ParentAccountId,
                ParentAccountName = a.ParentAccount != null ? a.ParentAccount.AccountName : null,
                a.Description,
                a.OpeningBalance,
                a.IsActive,
                HasChildren = a.ChildAccounts.Any()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var leafBalanceMap = await GetRunningBalanceMapAsync(companyId, cancellationToken);
        var hasJournalLines = await HasJournalLinesAsync(id, cancellationToken);
        var isLinkedToBank = await IsLinkedToBankAsync(id, companyId, cancellationToken);
        var (openingBalance, runningBalance) = await GetDisplayBalancesAsync(
            id,
            account.OpeningBalance,
            account.HasChildren,
            leafBalanceMap,
            cancellationToken);

        openingBalance = NormalizeBalanceForDisplay(openingBalance, account.TypeId);
        runningBalance = NormalizeBalanceForDisplay(runningBalance, account.TypeId);

        return new ChartOfAccountDto(
            account.Id,
            account.AccountNumber,
            account.AccountName,
            account.TypeId,
            account.TypeName,
            account.SubTypeId,
            account.SubTypeName,
            account.ParentAccountId,
            account.ParentAccountName,
            account.Description,
            openingBalance,
            runningBalance,
            account.IsActive,
            account.HasChildren,
            account.HasChildren,
            hasJournalLines,
            isLinkedToBank);
    }

    public async Task<ChartOfAccountDto?> GetByAccountNumberAsync(
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return null;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var id = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == accountNumber.Trim())
            .Select(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id == 0 ? null : await GetByIdAsync(id, cancellationToken);
    }

    public async Task<SuggestedAccountNumberDto> SuggestAccountNumberAsync(
        int typeId,
        int subTypeId,
        int? parentAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var typeValidation = await TryValidateTypeAndSubTypeAsync(typeId, subTypeId, cancellationToken);
        if (!typeValidation.Success)
        {
            return new SuggestedAccountNumberDto(GetRangeStart(typeId).ToString());
        }

        if (parentAccountId.HasValue)
        {
            var parent = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.Id == parentAccountId.Value && a.CompanyId == companyId)
                .Select(a => new { a.AccountNumber })
                .FirstOrDefaultAsync(cancellationToken);

            if (parent is null)
            {
                return new SuggestedAccountNumberDto(GetRangeStart(typeId).ToString());
            }

            var childNumbers = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.CompanyId == companyId && a.ParentAccountId == parentAccountId.Value)
                .Select(a => a.AccountNumber)
                .ToListAsync(cancellationToken);

            var parentNumber = ParseAccountNumber(parent.AccountNumber) ?? GetRangeStart(typeId);
            var nextChild = childNumbers
                .Select(ParseAccountNumber)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .DefaultIfEmpty(parentNumber)
                .Max() + 1;

            if (TypeNumberRanges.TryGetValue(typeId, out var childRange) && nextChild > childRange.Max)
            {
                nextChild = childRange.Max;
            }

            return new SuggestedAccountNumberDto(
                await EnsureAvailableAccountNumberAsync(companyId, nextChild.ToString(), typeId, cancellationToken));
        }

        var existingNumbers = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.TypeId == typeId && a.SubTypeId == subTypeId && a.ParentAccountId == null)
            .Select(a => a.AccountNumber)
            .ToListAsync(cancellationToken);

        if (existingNumbers.Count > 0)
        {
            var max = existingNumbers
                .Select(ParseAccountNumber)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .DefaultIfEmpty(GetRangeStart(typeId))
                .Max();

            var next = max + 100;
            if (TypeNumberRanges.TryGetValue(typeId, out var range) && next > range.Max)
            {
                next = max + 1;
            }

            return new SuggestedAccountNumberDto(
                await EnsureAvailableAccountNumberAsync(companyId, next.ToString(), typeId, cancellationToken));
        }

        if (TypeNumberRanges.TryGetValue(typeId, out var typeRange))
        {
            var subtypeOffset = Math.Min(subTypeId % 100, 99);
            var suggested = typeRange.Min + (subtypeOffset * 10);
            if (suggested > typeRange.Max)
            {
                suggested = typeRange.Min;
            }

            while (existingNumbers.Contains(suggested.ToString()))
            {
                suggested++;
            }

            return new SuggestedAccountNumberDto(
                await EnsureAvailableAccountNumberAsync(companyId, suggested.ToString(), typeId, cancellationToken));
        }

        return new SuggestedAccountNumberDto(
            await EnsureAvailableAccountNumberAsync(companyId, "1000", typeId, cancellationToken));
    }

    public async Task<ChartOfAccountSaveResult> CreateAsync(
        ChartOfAccountSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(request, null, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var now = DateTime.UtcNow;
        var user = _currentUser.UserName ?? "system";

        var entity = new ChartOfAccount
        {
            CompanyId = companyId,
            AccountNumber = request.AccountNumber.Trim(),
            AccountName = request.AccountName.Trim(),
            TypeId = request.TypeId,
            SubTypeId = request.SubTypeId,
            ParentAccountId = request.ParentAccountId,
            Description = request.Description?.Trim(),
            OpeningBalance = request.OpeningBalance,
            IsActive = request.IsActive,
            CreatedAt = now,
            CreatedBy = user
        };

        try
        {
            await _unitOfWork.Repository<ChartOfAccount>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (request.ParentAccountId.HasValue)
            {
                await ZeroParentOpeningBalanceAsync(request.ParentAccountId.Value, companyId, cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create chart of account {AccountNumber}", request.AccountNumber);
            return new ChartOfAccountSaveResult(false, "Could not save account. Check account type, sub-type, and company selection.", null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new ChartOfAccountSaveResult(true, null, dto);
    }

    public async Task<ChartOfAccountSaveResult> UpdateAsync(
        ChartOfAccountSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new ChartOfAccountSaveResult(false, "Account id is required.", null);
        }

        var validation = await ValidateSaveRequestAsync(request, request.Id.Value, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.Id == request.Id.Value && a.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new ChartOfAccountSaveResult(false, "Account not found.", null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.AccountNumber,
            entity.AccountName,
            entity.TypeId,
            entity.SubTypeId,
            entity.Description,
            entity.OpeningBalance,
            entity.IsActive
        });

        var hasChildren = await HasChildrenAsync(entity.Id, cancellationToken);

        if (!IsSuperAdmin()
            && (entity.TypeId != request.TypeId || entity.SubTypeId != request.SubTypeId))
        {
            return new ChartOfAccountSaveResult(
                false,
                "Only SuperAdmin can change account type or sub-type.",
                null);
        }

        entity.AccountNumber = request.AccountNumber.Trim();
        entity.AccountName = request.AccountName.Trim();
        entity.TypeId = request.TypeId;
        entity.SubTypeId = request.SubTypeId;
        entity.ParentAccountId = request.ParentAccountId;
        entity.Description = request.Description?.Trim();
        entity.OpeningBalance = hasChildren ? 0m : request.OpeningBalance;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        try
        {
            _unitOfWork.Repository<ChartOfAccount>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (request.ParentAccountId.HasValue)
            {
                await ZeroParentOpeningBalanceAsync(request.ParentAccountId.Value, companyId, cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update chart of account {AccountId}", request.Id);
            return new ChartOfAccountSaveResult(false, "Could not update account. Check account type and sub-type.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new ChartOfAccountSaveResult(true, null, dto);
    }

    public async Task<ChartOfAccountSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }
        var entity = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new ChartOfAccountSaveResult(false, "Account not found.", null);
        }

        if (await HasJournalLinesAsync(id, cancellationToken))
        {
            return new ChartOfAccountSaveResult(
                false,
                "Cannot delete this account because it has journal entry lines.",
                null);
        }

        if (await IsLinkedToBankAsync(id, companyId, cancellationToken))
        {
            return new ChartOfAccountSaveResult(
                false,
                "Cannot delete this account because it is linked to a bank account.",
                null);
        }

        if (await HasChildrenAsync(id, cancellationToken))
        {
            return new ChartOfAccountSaveResult(
                false,
                "Cannot delete this account because child accounts exist. Delete children first.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.AccountNumber,
            entity.AccountName,
            entity.TypeId,
            entity.SubTypeId
        });

        _unitOfWork.Repository<ChartOfAccount>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), oldSnapshot, null, cancellationToken);

        return new ChartOfAccountSaveResult(true, "Account deleted successfully.", null);
    }

    public async Task<ChartOfAccountLedgerDto?> GetLedgerAsync(
        int id,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var account = await GetByIdAsync(id, cancellationToken);
        if (account is null || account.IsGroupAccount)
        {
            return null;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();
        var rawOpening = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == id && a.CompanyId == companyId)
            .Select(a => a.OpeningBalance)
            .FirstOrDefaultAsync(cancellationToken);

        var from = fromDate?.Date;
        var to = toDate?.Date;
        var entries = new List<ChartOfAccountLedgerEntryDto>();
        var liabilityStyle = UsesCreditNormalDisplay(account.TypeId);

        decimal rawPeriodOpening;
        if (from.HasValue)
        {
            rawPeriodOpening = await GetBalanceBeforeDateAsync(id, companyId, from.Value, cancellationToken);
        }
        else
        {
            rawPeriodOpening = rawOpening;
        }

        var periodOpening = NormalizeBalanceForDisplay(rawPeriodOpening, account.TypeId);
        if (periodOpening != 0m)
        {
            var openingDate = from.HasValue ? from.Value.AddDays(-1) : DateTime.MinValue;
            var openingRef = from.HasValue ? "B/F" : "OPENING";
            var openingDesc = from.HasValue ? "Balance Brought Forward" : "Opening Balance";
            entries.Add(new ChartOfAccountLedgerEntryDto(
                openingDate,
                openingRef,
                openingDesc,
                periodOpening > 0 ? 0m : Math.Abs(periodOpening),
                periodOpening > 0 ? periodOpening : 0m,
                periodOpening));
        }

        var balance = periodOpening;
        var lineQuery = _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.ChartOfAccountId == id
                        && l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted);

        if (from.HasValue)
        {
            lineQuery = lineQuery.Where(l => l.JournalEntry.EntryDate >= from.Value);
        }

        if (to.HasValue)
        {
            lineQuery = lineQuery.Where(l => l.JournalEntry.EntryDate <= to.Value);
        }

        var lines = await lineQuery
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ThenBy(l => l.JournalEntry.Id)
            .ThenBy(l => l.Id)
            .Select(l => new
            {
                l.JournalEntry.EntryDate,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.Description,
                l.Debit,
                l.Credit,
                l.Memo
            })
            .ToListAsync(cancellationToken);

        decimal periodDebitTotal = 0m;
        decimal periodCreditTotal = 0m;

        foreach (var line in lines)
        {
            periodDebitTotal += line.Debit;
            periodCreditTotal += line.Credit;
            balance += liabilityStyle ? line.Credit - line.Debit : line.Debit - line.Credit;

            var description = !string.IsNullOrWhiteSpace(line.Memo)
                ? line.Memo
                : line.Description ?? "Journal Entry";

            entries.Add(new ChartOfAccountLedgerEntryDto(
                line.EntryDate,
                line.EntryNumber,
                description,
                line.Debit,
                line.Credit,
                balance));
        }

        var closingBalance = entries.Count > 0 ? entries[^1].Balance : periodOpening;

        return new ChartOfAccountLedgerDto(
            account,
            from,
            to,
            periodOpening,
            entries,
            closingBalance,
            periodDebitTotal,
            periodCreditTotal);
    }

    public async Task<byte[]?> ExportLedgerToExcelAsync(
        int id,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var ledger = await GetLedgerAsync(id, fromDate, toDate, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        var companyName = await GetCompanyNameAsync(cancellationToken);
        var periodLabel = BuildLedgerPeriodLabel(ledger.FromDate, ledger.ToDate);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Account Ledger");

        sheet.Cell(1, 1).Value = companyName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = "Account Ledger";
        sheet.Cell(3, 1).Value = $"Account: {ledger.Account.AccountNumber} — {ledger.Account.AccountName}";
        sheet.Cell(4, 1).Value = periodLabel;
        sheet.Cell(5, 1).Value = "Opening Balance:";
        sheet.Cell(5, 2).Value = ledger.OpeningBalance;
        sheet.Cell(5, 2).Style.NumberFormat.Format = "#,##0.00";
        sheet.Cell(5, 4).Value = "Closing Balance:";
        sheet.Cell(5, 5).Value = ledger.ClosingBalance;
        sheet.Cell(5, 5).Style.NumberFormat.Format = "#,##0.00";

        var headers = new[] { "Date", "Reference", "Description", "Debit", "Credit", "Balance" };
        const int headerRow = 7;
        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(headerRow, col + 1).Value = headers[col];
            sheet.Cell(headerRow, col + 1).Style.Font.Bold = true;
        }

        var rowIndex = headerRow + 1;
        foreach (var entry in ledger.Entries)
        {
            sheet.Cell(rowIndex, 1).Value = entry.Date == DateTime.MinValue
                ? string.Empty
                : entry.Date.ToString("dd/MM/yyyy");
            sheet.Cell(rowIndex, 2).Value = entry.Reference;
            sheet.Cell(rowIndex, 3).Value = entry.Description;
            sheet.Cell(rowIndex, 4).Value = entry.Debit;
            sheet.Cell(rowIndex, 5).Value = entry.Credit;
            sheet.Cell(rowIndex, 6).Value = entry.Balance;
            sheet.Cell(rowIndex, 4).Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(rowIndex, 5).Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(rowIndex, 6).Style.NumberFormat.Format = "#,##0.00";
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]?> ExportLedgerToPdfAsync(
        int id,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var pdfModel = await BuildAccountLedgerPdfModelAsync(id, fromDate, toDate, cancellationToken);
        return pdfModel is null ? null : _ledgerPdfService.GeneratePdf(pdfModel);
    }

    public async Task<byte[]> ExportToExcelAsync(CancellationToken cancellationToken = default)
    {
        var tree = await GetTreeAsync(cancellationToken: cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Chart of Accounts");

        var headers = new[]
        {
            "Account Type", "Sub Type", "Account Number", "Account Name", "Parent Account",
            "Opening Balance", "Running Balance", "Group Account", "Active"
        };

        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
            sheet.Cell(1, col + 1).Style.Font.Bold = true;
        }

        var rowIndex = 2;
        foreach (var type in tree)
        {
            foreach (var subType in type.SubTypes)
            {
                foreach (var account in subType.Accounts)
                {
                    rowIndex = WriteExportAccountRow(
                        sheet,
                        rowIndex,
                        type.TypeName,
                        subType.SubTypeName,
                        account,
                        0);
                }
            }
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int WriteExportAccountRow(
        IXLWorksheet sheet,
        int rowIndex,
        string typeName,
        string subTypeName,
        ChartOfAccountTreeAccountDto account,
        int depth)
    {
        var indent = new string(' ', depth * 2);
        var parentLabel = depth == 0 ? string.Empty : "Child";

        sheet.Cell(rowIndex, 1).Value = typeName;
        sheet.Cell(rowIndex, 2).Value = subTypeName;
        sheet.Cell(rowIndex, 3).Value = account.AccountNumber;
        sheet.Cell(rowIndex, 4).Value = indent + account.AccountName;
        sheet.Cell(rowIndex, 5).Value = parentLabel;
        sheet.Cell(rowIndex, 6).Value = account.OpeningBalance;
        sheet.Cell(rowIndex, 7).Value = account.RunningBalance;
        sheet.Cell(rowIndex, 8).Value = account.IsGroupAccount ? "Yes" : "No";
        sheet.Cell(rowIndex, 9).Value = account.IsActive ? "Yes" : "No";

        if (account.IsGroupAccount)
        {
            sheet.Range(rowIndex, 1, rowIndex, 9).Style.Font.Bold = true;
        }

        rowIndex++;

        foreach (var child in account.Children)
        {
            rowIndex = WriteExportAccountRow(sheet, rowIndex, typeName, subTypeName, child, depth + 1);
        }

        return rowIndex;
    }

    private async Task<Dictionary<int, decimal>> GetRunningBalanceMapAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var openingBalances = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId)
            .Select(a => new { a.Id, a.OpeningBalance })
            .ToListAsync(cancellationToken);

        var journalTotals = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted)
            .GroupBy(l => l.ChartOfAccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .ToListAsync(cancellationToken);

        var journalLookup = journalTotals.ToDictionary(x => x.AccountId, x => x.Debit - x.Credit);
        return openingBalances.ToDictionary(
            x => x.Id,
            x => x.OpeningBalance + journalLookup.GetValueOrDefault(x.Id, 0m));
    }

    private async Task<decimal> GetBalanceBeforeDateAsync(
        int accountId,
        int companyId,
        DateTime fromDate,
        CancellationToken cancellationToken)
    {
        var openingBalance = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == accountId && a.CompanyId == companyId)
            .Select(a => a.OpeningBalance)
            .FirstOrDefaultAsync(cancellationToken);

        var journalNet = await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .Where(l => l.ChartOfAccountId == accountId
                        && l.JournalEntry.CompanyId == companyId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && !l.JournalEntry.IsDeleted
                        && l.JournalEntry.EntryDate < fromDate)
            .Select(l => l.Debit - l.Credit)
            .SumAsync(cancellationToken);

        return Math.Round(openingBalance + journalNet, 2);
    }

    private async Task<bool> HasJournalLinesAsync(int accountId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<JournalEntryLine>()
            .Query()
            .AnyAsync(l => l.ChartOfAccountId == accountId, cancellationToken);

    private async Task<bool> IsLinkedToBankAsync(
        int accountId,
        int companyId,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<Bank>()
            .Query()
            .AnyAsync(b => b.CompanyId == companyId && b.ChartOfAccountId == accountId, cancellationToken);

    private async Task<ChartOfAccountSaveResult> ValidateSaveRequestAsync(
        ChartOfAccountSaveRequest request,
        int? existingId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            return new ChartOfAccountSaveResult(false, "Account number is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.AccountName))
        {
            return new ChartOfAccountSaveResult(false, "Account name is required.", null);
        }

        if (request.TypeId <= 0)
        {
            return new ChartOfAccountSaveResult(false, "Account type is required.", null);
        }

        if (request.SubTypeId <= 0)
        {
            return new ChartOfAccountSaveResult(false, "Sub-account type is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var typeValidation = await TryValidateTypeAndSubTypeAsync(request.TypeId, request.SubTypeId, cancellationToken);
        if (!typeValidation.Success)
        {
            return typeValidation;
        }

        var numberExists = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .AnyAsync(
                a => a.CompanyId == companyId
                     && a.AccountNumber == request.AccountNumber.Trim()
                     && (!existingId.HasValue || a.Id != existingId.Value),
                cancellationToken);

        if (numberExists)
        {
            var existing = await _unitOfWork.Repository<ChartOfAccount>()
                .Query()
                .Where(a => a.CompanyId == companyId && a.AccountNumber == request.AccountNumber.Trim())
                .Select(a => new { a.Id, a.AccountName })
                .FirstOrDefaultAsync(cancellationToken);

            return new ChartOfAccountSaveResult(
                false,
                $"Account number {request.AccountNumber.Trim()} already exists ({existing?.AccountName}).",
                null,
                existing?.Id);
        }

        if (existingId.HasValue && await HasChildrenAsync(existingId.Value, cancellationToken))
        {
            if (request.ParentAccountId.HasValue)
            {
                return new ChartOfAccountSaveResult(
                    false,
                    "Accounts with child accounts must remain main/header accounts.",
                    null);
            }

            if (request.OpeningBalance != 0m)
            {
                return new ChartOfAccountSaveResult(
                    false,
                    "Main/header accounts with children cannot have a direct opening balance.",
                    null);
            }
        }
        else if (request.ParentAccountId.HasValue)
        {
            var parentValidation = await ValidateParentAccountAsync(
                request.ParentAccountId.Value,
                request.TypeId,
                request.SubTypeId,
                companyId,
                existingId,
                cancellationToken);

            if (!parentValidation.Success)
            {
                return parentValidation;
            }
        }

        return new ChartOfAccountSaveResult(true, null, null);
    }

    private async Task<ChartOfAccountSaveResult> ValidateParentAccountAsync(
        int parentAccountId,
        int typeId,
        int subTypeId,
        int companyId,
        int? existingId,
        CancellationToken cancellationToken)
    {
        if (existingId.HasValue && parentAccountId == existingId.Value)
        {
            return new ChartOfAccountSaveResult(false, "An account cannot be its own parent.", null);
        }

        var parent = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.Id == parentAccountId && a.CompanyId == companyId)
            .Select(a => new { a.TypeId, a.SubTypeId, a.ParentAccountId })
            .FirstOrDefaultAsync(cancellationToken);

        if (parent is null)
        {
            return new ChartOfAccountSaveResult(false, "Selected parent account was not found.", null);
        }

        if (parent.ParentAccountId.HasValue)
        {
            return new ChartOfAccountSaveResult(false, "Parent must be a main/header account (one level only).", null);
        }

        if (parent.TypeId != typeId || parent.SubTypeId != subTypeId)
        {
            return new ChartOfAccountSaveResult(false, "Parent account must have the same type and sub-type.", null);
        }

        return new ChartOfAccountSaveResult(true, null, null);
    }

    private sealed record AccountRow(
        int Id,
        string AccountNumber,
        string AccountName,
        int? TypeId,
        int? SubTypeId,
        int? ParentAccountId,
        decimal OpeningBalance,
        bool IsActive);

    private static void CollectTreeAccountIds(
        ChartOfAccountTreeAccountDto account,
        ISet<int> includedIds)
    {
        includedIds.Add(account.Id);
        foreach (var child in account.Children)
        {
            CollectTreeAccountIds(child, includedIds);
        }
    }

    private static ChartOfAccountTreeAccountDto BuildTreeNode(
        AccountRow account,
        ILookup<int?, AccountRow> childrenLookup,
        IReadOnlyDictionary<int, decimal> leafBalanceMap)
    {
        var children = childrenLookup[account.Id]
            .OrderBy(c => c.AccountNumber)
            .Select(c => BuildTreeNode(c, childrenLookup, leafBalanceMap))
            .ToList();

        if (children.Count > 0)
        {
            return new ChartOfAccountTreeAccountDto(
                account.Id,
                account.AccountNumber,
                account.AccountName,
                children.Sum(c => c.OpeningBalance),
                children.Sum(c => c.RunningBalance),
                account.IsActive,
                true,
                account.ParentAccountId,
                children);
        }

        var rawRunning = leafBalanceMap.GetValueOrDefault(account.Id, account.OpeningBalance);

        return new ChartOfAccountTreeAccountDto(
            account.Id,
            account.AccountNumber,
            account.AccountName,
            NormalizeBalanceForDisplay(account.OpeningBalance, account.TypeId),
            NormalizeBalanceForDisplay(rawRunning, account.TypeId),
            account.IsActive,
            false,
            account.ParentAccountId,
            Array.Empty<ChartOfAccountTreeAccountDto>());
    }

    private static bool UsesCreditNormalDisplay(int? typeId) =>
        typeId is LiabilityTypeId or EquityTypeId;

    private static decimal NormalizeBalanceForDisplay(decimal netBalance, int? typeId) =>
        UsesCreditNormalDisplay(typeId) ? -netBalance : netBalance;

    private async Task<(decimal OpeningBalance, decimal RunningBalance)> GetDisplayBalancesAsync(
        int accountId,
        decimal ownOpening,
        bool hasChildren,
        IReadOnlyDictionary<int, decimal> leafBalanceMap,
        CancellationToken cancellationToken)
    {
        if (!hasChildren)
        {
            return (ownOpening, leafBalanceMap.GetValueOrDefault(accountId, ownOpening));
        }

        var childIds = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.ParentAccountId == accountId)
            .Select(a => new { a.Id, a.OpeningBalance })
            .ToListAsync(cancellationToken);

        decimal opening = 0m;
        decimal running = 0m;

        foreach (var child in childIds)
        {
            var childHasChildren = await HasChildrenAsync(child.Id, cancellationToken);
            var childBalances = await GetDisplayBalancesAsync(
                child.Id,
                child.OpeningBalance,
                childHasChildren,
                leafBalanceMap,
                cancellationToken);
            opening += childBalances.OpeningBalance;
            running += childBalances.RunningBalance;
        }

        return (opening, running);
    }

    private async Task<bool> HasChildrenAsync(int accountId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .AnyAsync(a => a.ParentAccountId == accountId, cancellationToken);

    private async Task ZeroParentOpeningBalanceAsync(
        int parentAccountId,
        int companyId,
        CancellationToken cancellationToken)
    {
        var parent = await _unitOfWork.Repository<ChartOfAccount>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(
                a => a.Id == parentAccountId && a.CompanyId == companyId,
                cancellationToken);

        if (parent is null || parent.OpeningBalance == 0m)
        {
            return;
        }

        parent.OpeningBalance = 0m;
        parent.UpdatedAt = DateTime.UtcNow;
        parent.UpdatedBy = _currentUser.UserName;
        _unitOfWork.Repository<ChartOfAccount>().Update(parent);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<ChartOfAccountSaveResult> TryValidateTypeAndSubTypeAsync(
        int typeId,
        int subTypeId,
        CancellationToken cancellationToken)
    {
        var subType = await _unitOfWork.Repository<SubAccountType>()
            .Query()
            .Where(s => s.SubTypeId == subTypeId)
            .Select(s => new { s.SubTypeId, s.TypeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (subType is null)
        {
            return new ChartOfAccountSaveResult(false, "Invalid sub-account type.", null);
        }

        if (subType.TypeId != typeId)
        {
            return new ChartOfAccountSaveResult(
                false,
                "Sub-account type does not belong to the selected account type.",
                null);
        }

        return new ChartOfAccountSaveResult(true, null, null);
    }

    private static int GetRangeStart(int typeId) =>
        TypeNumberRanges.TryGetValue(typeId, out var range) ? range.Min : 1000;

    private static int? ParseAccountNumber(string accountNumber) =>
        int.TryParse(accountNumber, out var value) ? value : null;

    private async Task<string> EnsureAvailableAccountNumberAsync(
        int companyId,
        string candidate,
        int typeId,
        CancellationToken cancellationToken)
    {
        var takenNumbers = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId)
            .Select(a => a.AccountNumber)
            .ToListAsync(cancellationToken);

        var takenSet = takenNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = candidate;

        while (takenSet.Contains(next))
        {
            var parsed = ParseAccountNumber(next) ?? GetRangeStart(typeId);
            parsed++;
            if (TypeNumberRanges.TryGetValue(typeId, out var range) && parsed > range.Max)
            {
                break;
            }

            next = parsed.ToString();
        }

        return next;
    }

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));

    private bool TryGetCompanyId(out int companyId, out ChartOfAccountSaveResult? error)
    {
        if (!_currentCompany.CompanyId.HasValue)
        {
            companyId = 0;
            error = new ChartOfAccountSaveResult(
                false,
                "No company is selected. Please choose a company from the top navbar.",
                null);
            return false;
        }

        companyId = _currentCompany.CompanyId.Value;
        error = null;
        return true;
    }

    private async Task TryAuditAsync(
        string action,
        string recordId,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(action, "ChartOfAccounts", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for chart of account {RecordId}", recordId);
        }
    }

    private async Task<PartyLedgerPdfDto?> BuildAccountLedgerPdfModelAsync(
        int id,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var ledger = await GetLedgerAsync(id, fromDate, toDate, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        var companyName = await GetCompanyNameAsync(cancellationToken);
        var periodLabel = BuildLedgerPeriodLabel(ledger.FromDate, ledger.ToDate);

        return new PartyLedgerPdfDto(
            "Account Ledger",
            ledger.Account.AccountName,
            ledger.Account.AccountNumber,
            null,
            companyName,
            periodLabel,
            ledger.OpeningBalance,
            ledger.ClosingBalance,
            false,
            ledger.Entries.Select(e => new PartyLedgerPdfLineDto(
                e.Date,
                e.Reference,
                e.Description,
                e.Debit,
                e.Credit,
                e.Balance)).ToList());
    }

    private async Task<string> GetCompanyNameAsync(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        return await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId)
            .Select(c => c.CompanyName)
            .FirstAsync(cancellationToken);
    }

    private static string BuildLedgerPeriodLabel(DateTime? fromDate, DateTime? toDate) =>
        fromDate.HasValue && toDate.HasValue
            ? $"Period: {fromDate.Value:dd/MM/yyyy} to {toDate.Value:dd/MM/yyyy}"
            : $"Full ledger as of {DateTime.Today:dd/MM/yyyy}";
}
