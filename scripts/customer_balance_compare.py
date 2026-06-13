#!/usr/bin/env python3
"""Compare QB Customer Balance Summary with ERP customer balances (company 3)."""
import csv
import re
import subprocess
from decimal import Decimal, ROUND_HALF_UP

QB_FILE = r"C:\Users\Muhammad Israr Ali\OneDrive\Desktop\QBExport 2026-05-31\Customer Balance Summary 12-06-2026.CSV"
AS_OF = "2026-06-12"
COMPANY_ID = 3
TOLERANCE = Decimal("0.05")


def parse_decimal(s):
    if not s or not str(s).strip():
        return Decimal("0")
    s = str(s).strip().strip('"').replace(",", "")
    try:
        return Decimal(s)
    except Exception:
        return Decimal("0")


def normalize_name(name):
    s = name.strip().strip('"').lower()
    s = re.sub(r"\s+", " ", s)
    s = re.sub(r"[^\w\s#\-()]", "", s)
    return s


def load_qb_balances():
    balances = {}
    with open(QB_FILE, encoding="cp1252") as f:
        for row in csv.reader(f):
            if not row or len(row) < 2:
                continue
            label = row[0].strip().strip('"')
            if not label or label.upper() == "TOTAL":
                continue
            if label.startswith(","):
                continue
            balances[label] = parse_decimal(row[1])
    return balances


def load_erp_balances():
    sql = f"""
SET NOCOUNT ON;
DECLARE @AsOf DATE = '{AS_OF}';
SELECT c.BuyerName,
  c.OpeningBalance
  + ISNULL((SELECT SUM(CASE WHEN si.InvoiceType=2 THEN -si.NetTotal ELSE si.NetTotal END)
    FROM SalesInvoices si WHERE si.CustomerId=c.Id AND si.CompanyId={COMPANY_ID}
    AND si.IsDeleted=0 AND si.Status=2 AND si.InvoiceDate<=@AsOf),0)
  - ISNULL((SELECT SUM(cr.Amount) FROM CustomerReceipts cr
    WHERE cr.CustomerId=c.Id AND cr.CompanyId={COMPANY_ID} AND cr.IsDeleted=0
    AND cr.ReceiptDate<=@AsOf
    AND NOT (cr.PaymentMethod=2 AND (cr.Status<>2 OR cr.ClearedAt IS NULL))),0)
  + ISNULL((SELECT SUM(bt.CustomerBalanceEffect) FROM BankTransactions bt
    WHERE bt.CustomerId=c.Id AND bt.CompanyId={COMPANY_ID} AND bt.IsDeleted=0
    AND bt.JournalEntryId IS NOT NULL AND bt.TransactionDate<=@AsOf),0) AS Bal
FROM Customers c WHERE c.CompanyId={COMPANY_ID} AND c.IsDeleted=0;
"""
    result = subprocess.run(
        ["sqlcmd", "-S", "localhost", "-d", "PakistanAccountingERP", "-E", "-W", "-s|", "-Q", sql],
        capture_output=True,
        text=True,
        check=True,
    )
    balances = {}
    for line in result.stdout.splitlines():
        if "|" not in line or line.startswith("BuyerName"):
            continue
        parts = line.split("|")
        if len(parts) < 2:
            continue
        name = parts[0].strip()
        if not name:
            continue
        try:
            bal = Decimal(parts[1].strip())
        except Exception:
            continue
        balances[name] = bal
    return balances


def main():
    qb = load_qb_balances()
    erp = load_erp_balances()

    qb_norm = {normalize_name(k): (k, v) for k, v in qb.items()}
    erp_norm = {normalize_name(k): (k, v) for k, v in erp.items()}

    qb_total = sum(qb.values())
    erp_total = sum(erp.values())

    print("=" * 80)
    print(f"CUSTOMER BALANCE COMPARISON - Company {COMPANY_ID} as of {AS_OF}")
    print("=" * 80)
    print(f"QB customers: {len(qb)}  Total: {qb_total:,.2f}")
    print(f"ERP customers: {len(erp)}  Total: {erp_total:,.2f}")
    print(f"Total diff (ERP - QB): {(erp_total - qb_total):,.2f}")
    print(f"AR control (11110) ERP GL: query separately")
    print()

    mismatches = []
    matched = 0

    all_keys = set(qb_norm.keys()) | set(erp_norm.keys())
    for key in sorted(all_keys):
        qb_item = qb_norm.get(key)
        erp_item = erp_norm.get(key)
        if qb_item and erp_item:
            qb_name, qb_bal = qb_item
            erp_name, erp_bal = erp_item
            diff = (erp_bal - qb_bal).quantize(Decimal("0.01"), ROUND_HALF_UP)
            if abs(diff) <= TOLERANCE:
                matched += 1
            else:
                mismatches.append((abs(diff), qb_name, qb_bal, erp_name, erp_bal, diff))
        elif qb_item:
            qb_name, qb_bal = qb_item
            mismatches.append((abs(qb_bal), qb_name, qb_bal, "MISSING IN ERP", Decimal("0"), -qb_bal))
        else:
            erp_name, erp_bal = erp_item
            mismatches.append((abs(erp_bal), "MISSING IN QB", Decimal("0"), erp_name, erp_bal, erp_bal))

    mismatches.sort(key=lambda x: x[0], reverse=True)

    print(f"Matched within tolerance: {matched}")
    print(f"Mismatched / missing: {len(mismatches)}")
    print()
    print("TOP MISMATCHES (|diff| >= 1.00):")
    print(f"{'QB Name':<45} {'QB Bal':>14} {'ERP Bal':>14} {'Diff':>12}")
    print("-" * 90)
    shown = 0
    for item in mismatches:
        if item[0] < Decimal("1.00"):
            continue
        _, qb_name, qb_bal, erp_name, erp_bal, diff = item
        erp_label = erp_name if erp_name == "MISSING IN ERP" else f"{erp_bal:,.2f}"
        if erp_name == "MISSING IN ERP":
            erp_label = "N/A"
        elif qb_name == "MISSING IN QB":
            qb_name = erp_name
            qb_bal = Decimal("0")
            erp_label = f"{erp_bal:,.2f}"
        print(f"{qb_name[:45]:<45} {qb_bal:>14,.2f} {str(erp_label):>14} {diff:>12,.2f}")
        shown += 1
        if shown >= 25:
            break

    sum_diff = sum(m[5] for m in mismatches)
    print()
    print(f"Sum of all diffs (ERP - QB): {sum_diff:,.2f}")


if __name__ == "__main__":
    main()
