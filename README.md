# Pakistan Accounting ERP

ASP.NET Core MVC (.NET 8) ERP aligned with the **PakistanAccountingERP_Schema_v6** SQL Server database.

## Solution structure

```
src/
  Domain/           Entities, enums, base classes
  Application/      Business logic (Step 3+)
  Infrastructure/   EF Core, repositories, seed data
  Web/              ASP.NET Core MVC
```

## Step 1–2 completed

- Domain entities matching v6 schema (27 entities + Identity)
- Enums: CustomerType, InvoiceType, CostingMethod, InventoryTransactionType, BankTransactionType, InvoiceStatus, BillStatus, ItemType, JournalStatus
- `AuditableEntity` / `CompanyAuditableEntity` with global soft-delete query filters
- `AppDbContext` + Fluent API configurations
- `DbInitializer` seed: provinces, UOM, account types, sub-account types, FBR scenarios, roles, permissions, demo company, tax settings, chart of accounts, admin user

## Run in Visual Studio

If you see **"A project with an Output Type of Class Library cannot be started directly"**:

1. Open **Solution Explorer**
2. Right-click **`PakistanAccountingERP.Web`** (`src/Web`)
3. Choose **Set as Startup Project** (name becomes **bold**)
4. Press **F5**

Only **Web** is runnable. `Domain`, `Application`, and `Infrastructure` are class libraries.

## Database setup

### Option A — EF Core migrations (recommended for new projects)

```powershell
cd "C:\Users\Muhammad Israr Ali\PakistanAccountingERP"
dotnet ef migrations add InitialCreate --project src/Infrastructure --startup-project src/Web
dotnet ef database update --project src/Infrastructure --startup-project src/Web
dotnet run --project src/Web
```

### Option B — Database created from SQL script (`Schema_v6.sql`)

If tables already exist from the SQL script, set in `appsettings.json`:

```json
"Database": {
  "ApplyMigrationsOnStartup": false,
  "SeedOnStartup": true
}
```

Or mark `InitialCreate` as applied, then let EF run the remaining migrations (recommended if you need tables added after v6, e.g. vendor payments):

```sql
USE [PakistanAccountingERP];
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260606095352_InitialCreate', N'8.0.11');
```

Then run:

```powershell
dotnet ef database update --project src/Infrastructure --startup-project src/Web
```

> **Note:** `Schema_v6.sql` names the inventory index `IX_InventoryTx_ItemId_Date`, not `IX_InventoryTransactions_ItemId`. The `AddWarehouseInventoryConfig` migration handles both SQL-script and EF-created databases.

**What `MigrateAsync` does:** applies any pending EF Core migrations to bring the database schema in line with the code. It runs automatically on startup when `ApplyMigrationsOnStartup` is `true`.

## Default login

| Field | Value |
|-------|-------|
| Email | admin@demo.com |
| Password | Admin@123 |
| Role | SuperAdmin |

## Step 3–4 completed

- `IRepository<T>`, `IUnitOfWork`, `ICurrentCompanyService`, `ICompanyScopedRepository<T>`
- `Repository<T>`, `UnitOfWork`, `CompanyScopedRepository<T>` with soft-delete on remove
- `AppConstants`, `SessionKeys`, `ReferenceTypes`
- Registered in `DependencyInjection.cs`

### Usage example

```csharp
public class CustomerService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentCompanyService _company;

    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct)
    {
        var companyId = _company.GetRequiredCompanyId();
        return await _uow.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId)
            .ToListAsync(ct);
    }
}
```

## Step 5 completed

| Service | Purpose |
|---------|---------|
| `ILookupService` | Provinces, UOM, scenarios, account types |
| `ICompanyService` | User companies, switch company |
| `IAuditService` | Action/login/error audit logs |
| `IPermissionService` | Role permission checks (memory cached) |
| `ICurrentUserService` | Logged-in user + roles + IP |
| `ICurrentCompanyService` | Session-based company context |

## Step 6 completed

- Login / Logout / Access Denied views (blue & white theme)
- `IAuthService` — sign-in, default company selection, audit login
- Cookie auth with login/access-denied paths
- `[RequirePermission("Sales.Create")]` attribute + handler
- `<button permission="Sales.Delete">` tag helper
- Global auth required (fallback policy); `AllowAnonymous` on login
- Company switch API: `GET /api/company/list`, `POST /api/company/switch/{id}`

**Demo login:** `admin@demo.com` / `Admin@123`

## Step 7–8 completed

- Fixed left **sidebar** with collapsible menu groups (permission-filtered)
- **Top navbar**: company switcher (AJAX), notifications, user menu, logout
- **Breadcrumb** bar below navbar
- **Blue & white theme** (`site.css`)
- CDN libs: Font Awesome 6, DataTables, Chart.js, Select2, Flatpickr
- Dashboard placeholder with KPI cards + Chart.js bar chart
- `app.js` — sidebar toggle + company switcher

## Step 10 completed

- **`IDashboardService`** — company-scoped KPIs, chart data, top customers, low stock, recent invoices
- **API:** `GET /api/dashboard` (+ granular `/summary`, `/monthly-sales`, etc.)
- **Dashboard view** loads all sections via AJAX (`dashboard.js` + Chart.js)
- KPI cards: today's sales, month sales, receivables, payables, inventory value

> Receivables/payables exclude receipts/payments until those modules are built (Step 18–19).

## Step 11 completed

- **`IChartOfAccountsService`** — tree view, CRUD, running balances, Excel export
- **MVC:** `/ChartOfAccounts` — Bootstrap accordion tree (Type → Sub-Type → Accounts)
- **API:** `/api/chart-of-accounts/*` — tree, CRUD, suggest number, export
- **Lookup API:** `/api/lookup/account-types`, `/api/lookup/sub-account-types`
- Account number auto-suggest by type/sub-type range
- Delete blocked if journal lines or bank link exists
- Running balance = opening + posted journal debits − credits
- Export to Excel (ClosedXML)

## Step 12 completed

- **`ICustomerService`** — CRUD, auto `CUST-0001` buyer IDs, ledger, date-range statement
- **MVC:** `/Customers` list (DataTables server-side), `/Customers/Ledger/{id}`, `/Customers/Statement/{id}`
- **API:** `/api/customers/*` — datatable, CRUD, ledger, statement
- Customer ledger: opening balance + posted invoices with running balance
- Opening balance posts to **Accounts Receivable (1200)** via system journal entry
- Customer receipts credit AR and debit Cash/Bank; bank balance updated when applicable
- Statement: date filter + print-friendly view (Save as PDF via browser)
- Delete blocked when sales invoices exist

## Step 13 completed

- **`IVendorService`** — CRUD, auto `VEND-0001` codes, 18% default tax rate, ledger, statement
- **MVC:** `/Vendors` list (DataTables), `/Vendors/Ledger/{id}`, `/Vendors/Statement/{id}`
- **API:** `/api/vendors/*` — datatable, CRUD, ledger, statement
- Vendor ledger: opening balance + approved bills with running payable balance
- Opening balance posts to **Accounts Payable (2100)** via system journal entry
- Vendor payments debit AP and credit Cash/Bank; bank balance reduced when applicable
- Statement: date filter + print-friendly view
- Delete blocked when vendor bills exist

## Step 14 completed

- **`IItemService`** — CRUD, auto `ITEM-0001` codes, category/UOM lookups, opening stock on create
- **`IItemCategoryService`** — CRUD for company-scoped categories
- **`IUnitOfMeasureService`** — CRUD for global units (KG, PCS, CTN, etc.)
- **MVC:** `/Items`, `/ItemCategories`, `/UnitsOfMeasure` — DataTables + modal forms
- **API:** `/api/items/*`, `/api/item-categories/*`, `/api/units-of-measure/*`
- **Lookup:** `GET /api/lookup/units-of-measure`
- Delete blocked when item is used on invoices/bills/inventory; category blocked when items assigned; UOM blocked when items reference it
- Current stock is read-only after create (updated by inventory transactions later)

## Step 15 completed — Sales Invoices

- **`ISalesInvoiceService`** — create draft invoice, auto `INV-0001` numbers, customer/item lookups
- **Post to GL** — draft → posted; creates `JE-0001` journal entry:
  - Sales invoice: DR Accounts Receivable (1200), CR Sales Revenue (4100), CR Sales Tax Payable (2200)
  - Credit note: reversed using Sales Returns (4200)
- **FBR submission** — `IFbrSubmissionService`; simulation mode when company FBR URL/token not set; live HTTP POST when configured
- **Cancel** — draft invoices only
- **MVC:** `/SalesInvoices`, `/SalesInvoices/Create`, `/SalesInvoices/Details/{id}`
- **API:** `/api/sales-invoices/*` — datatable, create, detail, post, cancel, submit-fbr

## Step 16 completed — Warehouses & Stock Transactions

- **`IWarehouseService`** — CRUD, auto `WH-0001` codes, delete blocked when transactions exist
- **`IInventoryTransactionService`** — create stock in/out/opening/adjustment; updates item `CurrentStock`
- **MVC:** `/Warehouses`, `/InventoryTransactions` — DataTables + modal forms
- **API:** `/api/warehouses/*`, `/api/inventory-transactions/*`
- Demo warehouse `WH-0001` (Main Warehouse) seeded per company
- Adjustment supports signed quantity; stock out validates available quantity

## Step 17 completed — Vendor Bills

- **`IVendorBillService`** — create draft bill, auto `BILL-0001` numbers, vendor/item lookups
- **Approve to GL** — draft → approved; creates journal entry:
  - DR Purchases (5100), DR Input Tax Recoverable (1400), CR Accounts Payable (2100)
- **Cancel** — draft bills only
- **MVC:** `/VendorBills`, `/VendorBills/Create`, `/VendorBills/Details/{id}`
- **API:** `/api/vendor-bills/*` — datatable, create, detail, approve, cancel
- Sidebar: Purchase → Enter Bills, Purchase Bills

## Step 18 completed — Vendor Payments

- **`IVendorPaymentService`** — CRUD, auto `VPAY-0001` numbers, vendor/bank lookups
- **MVC:** `/VendorPayments` — DataTables list + modal create/edit
- **API:** `/api/vendor-payments/*` — datatable, CRUD, next number, vendor/bank lookups
- Vendor balance and ledger now include payments (reduces outstanding payable)
- Payment modes: Cash, Cheque, Bank Transfer (same as customer receipts)
- Delete blocked on vendor when payments exist

## Step 19 completed — Inventory Reports

- **`IInventoryReportService`** — stock summary, low stock alert, stock movement reports
- **MVC:** `/InventoryReports` hub with:
  - **Stock Summary** — on-hand qty and value (purchase rate), filter by category
  - **Low Stock Alert** — items below minimum or at reorder level
  - **Stock Movement** — transactions by date range with item/warehouse filters
- **API:** `/api/inventory-reports/*`
- Print-friendly layouts (Save as PDF via browser)
- Sidebar: Inventory → Inventory Reports

## Step 20 completed — Banking Module

- **`IBankService`** — CRUD bank accounts, optional GL link (Cash & Bank accounts), opening/current balance
- **`IBankTransactionService`** — deposit, withdrawal, transfer; updates bank balances
- **`IBankReconciliationService`** — preview unreconciled txns, complete reconciliation, history
- **MVC:** `/Banks`, `/BankTransactions`, `/BankReconciliations`
- **API:** `/api/banks/*`, `/api/bank-transactions/*`, `/api/bank-reconciliations/*`
- Demo bank account seeded per company (linked to GL 1100)
- Delete blocked when transactions or payment references exist

## Step 21 completed — Company & FBR Settings

- **`ICompanySettingsService`** — company profile, FBR API URL/token, default tax rates
- **MVC:** `/CompanySettings` — single page for company info, FBR e-invoicing, and tax settings
- **API:** `/api/company-settings` — GET/PUT
- FBR status badge: Live / Partial / Simulation mode
- API token stored securely (never returned on GET; update or clear explicitly)
- Tax rates update the company's `TaxSetting` record (used by sales invoices)
- Sidebar: Settings → Company Settings, Tax Settings (scrolls to tax section)

## Step 22 completed — Sales & Purchase Reports

- **`ISalesReportService`** — sales register, sales by customer, sales tax summary
- **`IPurchaseReportService`** — purchase register, purchase by vendor, input tax summary, stack &amp; lot tracking
- **MVC:** `/SalesReports`, `/PurchaseReports` — report hubs (purchase includes stack/lot tracking)
- **Stack &amp; Lot Tracking** — purchased vs sold cartons/weight by item, lot no, and stack no from vendor bills and sales invoices
- **API:** `/api/sales-reports/*`, `/api/purchase-reports/*`
- Date-range filters with customer/vendor lookup; posted/approved-only toggle
- Print-friendly layouts (Save as PDF via browser)
- Sidebar: Reports → Sales Reports, Purchase Reports, Inventory Reports

## Step 23 completed — Journal Entries UI

- **`IJournalEntryService`** — list, create manual entries, view details, post, delete
- **MVC:** `/JournalEntries` — list (DataTables), `/JournalEntries/Create`, `/JournalEntries/Details/{id}`
- **API:** `/api/journal-entries/*`
- Manual entries saved as **Draft**; post when debits = credits
- System entries from sales invoices and vendor bills shown with source links
- Sidebar: Journal Entries (below Chart of Accounts)
- Permissions: `JournalEntries.View/Create/Edit/Delete`

## Step 24 completed — Financial Reports

- **`IFinancialReportService`** — trial balance, profit &amp; loss, balance sheet from posted journal entries
- **MVC:** `/FinancialReports` — report hub with 3 reports
- **API:** `/api/financial-reports/trial-balance`, `/profit-and-loss`, `/balance-sheet`
- Date-range filters (default: current month); balance sheet uses as-of date
- P&amp;L: revenue (credits − debits), COGS/expenses (debits − credits), gross &amp; net profit
- Balance sheet: assets, liabilities, equity; YTD net income included under equity
- Print-friendly layouts (Save as PDF via browser)
- Sidebar: Reports → Financial Reports

## Next step

**Step 25** — user management, audit logs UI, or fiscal years.
