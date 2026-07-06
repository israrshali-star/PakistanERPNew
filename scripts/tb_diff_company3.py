"""Compute ERP trial balance debits/credits for company 3 using same rules as GlTrialBalanceColumns."""
import subprocess
import json

COMPANY_ID = 3
ASSET, LIABILITY, EQUITY, REVENUE, COGS, EXPENSE = 1, 2, 3, 4, 5, 6
AR, AP, OBE = "11110", "20000", "30000"
SALES_TAX = {"25500", "25510", "25520"}


def q(sql: str) -> list[str]:
    out = subprocess.check_output(
        ["sqlcmd", "-S", "localhost", "-d", "PakistanAccountingERP", "-E", "-W", "-h", "-1", "-Q", sql],
        text=True,
    )
    return [ln.strip() for ln in out.splitlines() if ln.strip()]


def uses_debit_column(stored_net: float, type_id: int, acct: str, company_id: int) -> bool:
    if company_id == 6 and acct == AR:
        return stored_net > 0
    if company_id == 6 and acct in SALES_TAX:
        return stored_net < 0
    if acct == AR:
        return stored_net < 0
    if acct == AP:
        return stored_net < 0
    if acct == OBE:
        return stored_net > 0
    if type_id == LIABILITY:
        return stored_net > 0
    if type_id in (EQUITY, REVENUE):
        return stored_net < 0
    return stored_net > 0


def compute_net(opening: float, dr: float, cr: float, type_id: int, acct: str) -> float:
    if type_id in (LIABILITY, EQUITY):
        return opening + (cr - dr)
    return opening + (dr - cr)


accounts = []
for line in q(
    f"""
    SET NOCOUNT ON;
    SELECT a.Id, a.AccountNumber, a.TypeId, a.OpeningBalance,
           ISNULL(j.Dr,0), ISNULL(j.Cr,0)
    FROM ChartOfAccounts a
    LEFT JOIN (
      SELECT l.ChartOfAccountId, SUM(l.Debit) Dr, SUM(l.Credit) Cr
      FROM JournalEntryLines l
      JOIN JournalEntries je ON je.Id=l.JournalEntryId
      WHERE je.CompanyId={COMPANY_ID} AND je.Status=2 AND je.IsDeleted=0
      GROUP BY l.ChartOfAccountId
    ) j ON j.ChartOfAccountId=a.Id
    WHERE a.CompanyId={COMPANY_ID} AND a.IsDeleted=0 AND a.IsActive=1
      AND NOT EXISTS (SELECT 1 FROM ChartOfAccounts c WHERE c.ParentAccountId=a.Id AND c.IsDeleted=0)
    ORDER BY a.AccountNumber;
    """
):
    parts = line.split()
    if len(parts) < 6:
        continue
    acct_id = int(parts[0])
    acct_num = parts[1]
    type_id = int(parts[2])
    opening = float(parts[3].replace(",", ""))
    dr = float(parts[4].replace(",", ""))
    cr = float(parts[5].replace(",", ""))
    net = round(compute_net(opening, dr, cr, type_id, acct_num), 2)
    amt = abs(net)
    if amt == 0:
        continue
    debit_col = amt if uses_debit_column(net, type_id, acct_num, COMPANY_ID) else 0
    credit_col = 0 if debit_col else amt
    accounts.append((acct_num, net, debit_col, credit_col))

total_dr = round(sum(a[2] for a in accounts), 2)
total_cr = round(sum(a[3] for a in accounts), 2)
print(f"Leaf accounts with balance: {len(accounts)}")
print(f"TB Debits:  {total_dr:,.2f}")
print(f"TB Credits: {total_cr:,.2f}")
print(f"Difference (Dr - Cr): {total_dr - total_cr:,.2f}")

if abs(total_dr - total_cr) > 0.01:
    print("\nLargest contributors to imbalance (by |net| on wrong side):")
    diff = total_dr - total_cr
    # If credits high, list accounts in credit column with largest amounts
    for acct_num, net, dr_col, cr_col in sorted(accounts, key=lambda x: -x[3])[:15]:
        if cr_col:
            print(f"  {acct_num} net={net:,.2f} -> Cr {cr_col:,.2f}")
