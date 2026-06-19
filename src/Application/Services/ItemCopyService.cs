using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;

namespace PakistanAccountingERP.Application.Services;

public class ItemCopyService : IItemCopyService
{
    private readonly IUnitOfWork _unitOfWork;

    public ItemCopyService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ItemCopyResult> CopyItemsAsync(
        int sourceCompanyId,
        IReadOnlyList<int> targetCompanyIds,
        CancellationToken cancellationToken = default)
    {
        if (targetCompanyIds.Count == 0)
        {
            return new ItemCopyResult(false, "At least one target company id is required.", 0, 0, 0);
        }

        var sourceExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == sourceCompanyId, cancellationToken);

        if (!sourceExists)
        {
            return new ItemCopyResult(false, $"Source company id {sourceCompanyId} was not found.", 0, 0, 0);
        }

        var sourceItems = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == sourceCompanyId)
            .OrderBy(i => i.ItemCode)
            .ToListAsync(cancellationToken);

        if (sourceItems.Count == 0)
        {
            return new ItemCopyResult(false, $"Source company {sourceCompanyId} has no items to copy.", 0, 0, 0);
        }

        var sourceCategories = await _unitOfWork.Repository<ItemCategory>()
            .Query()
            .Where(c => c.CompanyId == sourceCompanyId)
            .ToListAsync(cancellationToken);

        var categoryNameBySourceId = sourceCategories.ToDictionary(c => c.Id, c => c.Name);
        var now = DateTime.UtcNow;
        const string userName = "copy-items";
        var categoriesCreated = 0;
        var itemsCreated = 0;
        var itemsSkipped = 0;

        foreach (var targetCompanyId in targetCompanyIds.Distinct())
        {
            if (targetCompanyId == sourceCompanyId)
            {
                continue;
            }

            var targetExists = await _unitOfWork.Repository<Company>()
                .Query()
                .AnyAsync(c => c.Id == targetCompanyId, cancellationToken);

            if (!targetExists)
            {
                return new ItemCopyResult(
                    false,
                    $"Target company id {targetCompanyId} was not found.",
                    categoriesCreated,
                    itemsCreated,
                    itemsSkipped);
            }

            var targetCategories = await _unitOfWork.Repository<ItemCategory>()
                .Query(asNoTracking: false)
                .Where(c => c.CompanyId == targetCompanyId)
                .ToListAsync(cancellationToken);

            var categoryIdByName = targetCategories.ToDictionary(
                c => c.Name,
                c => c.Id,
                StringComparer.OrdinalIgnoreCase);

            foreach (var sourceCategory in sourceCategories)
            {
                if (categoryIdByName.ContainsKey(sourceCategory.Name))
                {
                    continue;
                }

                var category = new ItemCategory
                {
                    CompanyId = targetCompanyId,
                    Name = sourceCategory.Name,
                    Description = sourceCategory.Description,
                    CreatedAt = now,
                    CreatedBy = userName
                };

                await _unitOfWork.Repository<ItemCategory>().AddAsync(category, cancellationToken);
                categoryIdByName[sourceCategory.Name] = category.Id;
                categoriesCreated++;
            }

            var existingCodes = await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == targetCompanyId)
                .Select(i => i.ItemCode)
                .ToListAsync(cancellationToken);

            var existingCodeSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceItem in sourceItems)
            {
                if (existingCodeSet.Contains(sourceItem.ItemCode))
                {
                    itemsSkipped++;
                    continue;
                }

                int? targetCategoryId = null;
                if (sourceItem.ItemCategoryId.HasValue
                    && categoryNameBySourceId.TryGetValue(sourceItem.ItemCategoryId.Value, out var categoryName)
                    && categoryIdByName.TryGetValue(categoryName, out var mappedCategoryId))
                {
                    targetCategoryId = mappedCategoryId;
                }

                var item = new Item
                {
                    CompanyId = targetCompanyId,
                    ItemType = sourceItem.ItemType,
                    ItemCode = sourceItem.ItemCode,
                    ItemName = sourceItem.ItemName,
                    StackNo = sourceItem.StackNo,
                    LotNo = sourceItem.LotNo,
                    Description = sourceItem.Description,
                    HSCode = sourceItem.HSCode,
                    Barcode = sourceItem.Barcode,
                    UnitOfMeasureId = sourceItem.UnitOfMeasureId,
                    ItemCategoryId = targetCategoryId,
                    PurchaseRate = sourceItem.PurchaseRate,
                    SaleRate = sourceItem.SaleRate,
                    MinimumStock = sourceItem.MinimumStock,
                    ReorderLevel = sourceItem.ReorderLevel,
                    CurrentStock = 0m,
                    Cartons = 0m,
                    CostingMethod = sourceItem.CostingMethod,
                    IsActive = sourceItem.IsActive,
                    CreatedAt = now,
                    CreatedBy = userName
                };

                await _unitOfWork.Repository<Item>().AddAsync(item, cancellationToken);
                existingCodeSet.Add(sourceItem.ItemCode);
                itemsCreated++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ItemCopyResult(
            true,
            $"Copied {sourceItems.Count} item definition(s) from company {sourceCompanyId} to {targetCompanyIds.Count} target company/companies.",
            categoriesCreated,
            itemsCreated,
            itemsSkipped);
    }
}
