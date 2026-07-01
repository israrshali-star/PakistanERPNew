"""Simulate ERP trial balance debit/credit columns for company 6 openings."""
import subprocess

AR = "11110"
AP = "20000"
OBE = "30000"
ASSET = 1
LIABILITY = 2
EQUITY = 3


def uses_debit_column(stored_net: float, type_id: int, acct: str, company_id: int) -> bool:
    if company_id == 6:
        if acct == AR:
            return stored_net > 0
        if acct in ("25500", "25510", "25520"):
            return stored_net < 0

    if acct == AR:
        return stored_net < 0

    if acct == AP:
        return stored_net < 0

    if acct == OBE:
        return stored_net > 0

    if type_id == LIABILITY:
        return stored_net > 0

    if type_id in (EQUITY, 4):
        return stored_net < 0

    return stored_net > 0


def split(stored_net: float, type_id: int, acct: str, company_id: int) -> tuple[float, float]:
    amt = abs(stored_net)
    if amt == 0:
        return 0.0, 0.0
    if uses_debit_column(stored_net, type_id, acct, company_id):
        return amt, 0.0
    return 0.0, amt


q = """
SELECT c.CompanyId, c.AccountNumber, c.OpeningBalance, c.TypeId
FROM ChartOfAccounts c
WHERE c.CompanyId IN (3, 6) AND c.IsDeleted=0 AND c.IsActive=1
  AND NOT EXISTS (SELECT 1 FROM ChartOfAccounts p WHERE p.ParentAccountId=c.Id AND p.IsDeleted=0)
"""
out = subprocess.check_output(
    ["sqlcmd", "-S", "localhost", "-d", "PakistanAccountingERP", "-E", "-W", "-h", "-1", "-Q", q],
    text=True,
)
debits = credits = 0.0
rows = []
company = None
totals = {}
for line in out.splitlines():
    parts = line.split()
    if len(parts) < 4:
        continue
    co, acct, bal_s, type_s = parts[0], parts[1], parts[2].replace(",", ""), parts[3]
    try:
        bal = float(bal_s)
        type_id = int(type_s)
        company_id = int(co)
    except ValueError:
        continue
    if bal == 0:
        continue
    d, c = split(bal, type_id, acct, company_id)
    t = totals.setdefault(company_id, [0.0, 0.0])
    t[0] += d
    t[1] += c
    rows.append((company_id, acct, bal, d, c))

for company_id in sorted(totals):
    d, c = totals[company_id]
    print(f"\nCompany {company_id}: debits {d:,.2f} credits {c:,.2f} diff {d-c:,.2f}")
