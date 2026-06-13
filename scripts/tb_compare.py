#!/usr/bin/env python3
"""Compare QB Trial Balance CSV with ERP trial balance for company 3."""
import csv
import re
import subprocess
from decimal import Decimal, ROUND_HALF_UP

QB_FILE = r"C:\Users\Muhammad Israr Ali\OneDrive\Desktop\QBExport 2026-05-31\Trial Balance AS 12062026.CSV"
AS_OF = "2026-06-12"
COMPANY_ID = 3
TOLERANCE = Decimal("0.02")

QB_TO_ERP = {
    "10800": "10015",
    "10900": "10016",
    "12000": "10017",
    "15200": "15100",
    "30800": "30020",
}

SKIP_PARENTS = {"10000", "11000", "12100", "20000", "47900"}


def parse_decimal(s):
    if not s or not str(s).strip():
        return Decimal("0")
    s = (
        str(s)
        .strip()
        .strip('"')
        .replace(",", "")
        .replace("(", "-")
        .replace(")", "")
    )
    try:
        return Decimal(s)
    except Exception:
        return Decimal("0")


def extract_account(label):
    label = label.strip().strip('"')
    sub = re.search(r":(\d{4,5})\b", label)
    if sub:
        return sub.group(1)
    top = re.match(r"^\s*\"?(\d{4,5})\b", label)
    return top.group(1) if top else None


def is_parent_row(label, acct):
    clean = label.strip().strip('"')
    return acct in SKIP_PARENTS and f":{acct} " not in clean and f":{acct}·" not in clean


def parse_qb_trial_balance():
    accounts = {}
    labels = {}
    with open(QB_FILE, encoding="cp1252") as f:
        for row in csv.reader(f):
            if not row:
                continue
            label = row[0].strip().strip('"') if row else ""
            if (
                not label
                or label.upper().startswith("TOTAL")
                or label.lower() == "debit"
            ):
                continue
            acct = extract_account(label)
            if not acct:
                continue

            debit = parse_decimal(row[1] if len(row) > 1 else "")
            credit = parse_decimal(row[2] if len(row) > 2 else "")
            net = (debit - credit).quantize(Decimal("0.01"), ROUND_HALF_UP)

            if acct == "10000" and is_parent_row(label, acct):
                accounts["30000"] = net
                labels["30000"] = f"QB 10000 header -> ERP 30000 ({label})"
                continue

            if acct == "11000" and is_parent_row(label, acct):
                accounts["11110"] = net
                labels["11110"] = f"QB 11000 AR -> ERP 11110 ({label})"
                continue

            if acct == "20000" and is_parent_row(label, acct):
                accounts["20000"] = net
                labels["20000"] = f"QB 20000 AP ({label})"
                continue

            if is_parent_row(label, acct):
                continue

            erp_acct = QB_TO_ERP.get(acct, acct)
            accounts[erp_acct] = accounts.get(erp_acct, Decimal("0")) + net
            labels[erp_acct] = label
    return accounts, labels


def query_erp_trial_balance():
    sql = f"""
SET NOCOUNT ON;
DECLARE @CompanyId INT = {COMPANY_ID};
DECLARE @AsOf DATE = '{AS_OF}';

SELECT
    coa.AccountNumber,
    coa.AccountName,
    coa.OpeningBalance,
    ISNULL(SUM(jel.Debit), 0) AS TotalDebit,
    ISNULL(SUM(jel.Credit), 0) AS TotalCredit,
    coa.OpeningBalance + ISNULL(SUM(jel.Debit), 0) - ISNULL(SUM(jel.Credit), 0) AS ClosingNet
FROM ChartOfAccounts coa
LEFT JOIN (
    SELECT jel.ChartOfAccountId, jel.Debit, jel.Credit
    FROM JournalEntryLines jel
    INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
    WHERE je.CompanyId = @CompanyId AND je.Status = 2 AND je.IsDeleted = 0 AND je.EntryDate <= @AsOf
) jel ON jel.ChartOfAccountId = coa.Id
WHERE coa.CompanyId = @CompanyId AND coa.IsActive = 1
GROUP BY coa.Id, coa.AccountNumber, coa.AccountName, coa.OpeningBalance
HAVING ABS(coa.OpeningBalance + ISNULL(SUM(jel.Debit), 0) - ISNULL(SUM(jel.Credit), 0)) > 0.005
ORDER BY coa.AccountNumber;
"""
    result = subprocess.run(
        ["sqlcmd", "-S", "localhost", "-d", "PakistanAccountingERP", "-E", "-W", "-s", "|"],
        input=sql,
        capture_output=True,
        text=True,
        check=True,
    )
    accounts = {}
    meta = {}
    for line in result.stdout.splitlines():
        if "|" not in line or line.startswith("AccountNumber"):
            continue
        parts = [p.strip() for p in line.split("|")]
        if len(parts) < 6:
            continue
        acct, name, opening, td, tc, closing = parts[:6]
        if not acct or acct == "-":
            continue
        try:
            net = Decimal(closing)
            accounts[acct] = net
            meta[acct] = {
                "name": name,
                "opening": Decimal(opening),
                "debit": Decimal(td),
                "credit": Decimal(tc),
            }
        except Exception:
            continue
    return accounts, meta


def main():
    qb, qb_labels = parse_qb_trial_balance()
    erp, erp_meta = query_erp_trial_balance()

    all_accts = sorted(set(qb.keys()) | set(erp.keys()))
    matched = []
    mismatched = []

    for acct in all_accts:
        q = qb.get(acct)
        e = erp.get(acct)
        if q is None and e is None:
            continue
        if q is None:
            mismatched.append(
                {
                    "account": acct,
                    "qb_net": None,
                    "erp_net": e,
                    "diff": e,
                    "note": "ERP only",
                }
            )
            continue
        if e is None:
            mismatched.append(
                {
                    "account": acct,
                    "qb_net": q,
                    "erp_net": None,
                    "diff": -q,
                    "note": "QB only",
                }
            )
            continue
        diff = (e - q).quantize(Decimal("0.01"), ROUND_HALF_UP)
        if abs(diff) <= TOLERANCE:
            matched.append(acct)
        else:
            mismatched.append(
                {
                    "account": acct,
                    "qb_net": q,
                    "erp_net": e,
                    "diff": diff,
                    "note": "",
                }
            )

    qb_total_dr = sum(
        max(v, Decimal("0")) for v in qb.values()
    )
    qb_total_cr = sum(
        max(-v, Decimal("0")) for v in qb.values()
    )
    erp_total_dr = sum(
        max(v, Decimal("0")) for v in erp.values()
    )
    erp_total_cr = sum(
        max(-v, Decimal("0")) for v in erp.values()
    )

    print("=" * 80)
    print(f"TRIAL BALANCE COMPARISON - Company {COMPANY_ID} as of {AS_OF}")
    print("=" * 80)
    print(f"Matched accounts: {len(matched)}")
    print(f"Mismatched accounts: {len(mismatched)}")
    print(f"QB TB total (from file): 1102428325.69")
    print(f"ERP total debits (closing): {erp_total_dr:,.2f}")
    print(f"ERP total credits (closing): {erp_total_cr:,.2f}")
    print(f"QB computed total debits: {qb_total_dr:,.2f}")
    print(f"QB computed total credits: {qb_total_cr:,.2f}")
    print()

    mismatched.sort(key=lambda x: abs(x["diff"] or Decimal("0")), reverse=True)
    print("MISMATCHES ONLY (sorted by |diff|):")
    print(f"{'Acct':<8} {'QB Net':>18} {'ERP Net':>18} {'Diff':>18}  Note")
    print("-" * 80)
    for m in mismatched:
        q_s = f"{m['qb_net']:,.2f}" if m["qb_net"] is not None else "N/A"
        e_s = f"{m['erp_net']:,.2f}" if m["erp_net"] is not None else "N/A"
        d_s = f"{m['diff']:,.2f}"
        label = qb_labels.get(m["account"], erp_meta.get(m["account"], {}).get("name", m["note"]))
        label = (label or "")[:40].encode("ascii", "replace").decode("ascii")
        print(f"{m['account']:<8} {q_s:>18} {e_s:>18} {d_s:>18}  {label}")

    print()
    print("MATCHED ACCOUNTS:")
    for acct in sorted(matched):
        print(f"  {acct}: {qb[acct]:,.2f}")


if __name__ == "__main__":
    main()
