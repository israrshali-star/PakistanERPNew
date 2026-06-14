(function (window) {
    'use strict';

    var rowKind = {
        sectionHeader: 1,
        groupHeader: 2,
        account: 3,
        subtotal: 4,
        total: 5
    };

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatAmount(value) {
        if (value === null || value === undefined || value === '') {
            return '';
        }

        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatProformaDate(value) {
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return value;
        }

        return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: '2-digit' });
    }

    function formatProformaDateRange(fromDate, toDate) {
        var from = new Date(fromDate);
        var to = new Date(toDate);

        if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime())) {
            return '';
        }

        if (from.toDateString() === to.toDateString()) {
            return formatProformaDate(to);
        }

        if (from.getFullYear() === to.getFullYear() && from.getMonth() === to.getMonth()) {
            return from.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) +
                ' - ' +
                to.toLocaleDateString('en-US', { day: 'numeric', year: '2-digit' });
        }

        return formatProformaDate(from) + ' - ' + formatProformaDate(to);
    }

    function toInputDate(date) {
        return date.getFullYear() + '-' +
            String(date.getMonth() + 1).padStart(2, '0') + '-' +
            String(date.getDate()).padStart(2, '0');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }

        return body.message || body.Message || fallback;
    }

    function indentClass(level) {
        if (!level || level <= 0) {
            return '';
        }

        return 'indent-' + Math.min(level, 4);
    }

    function rowClass(kind) {
        switch (kind) {
            case rowKind.sectionHeader:
                return 'row-section';
            case rowKind.groupHeader:
                return 'row-group';
            case rowKind.subtotal:
                return 'row-subtotal';
            case rowKind.total:
                return 'row-total';
            default:
                return 'row-account';
        }
    }

    function renderAmountCell(value) {
        return value === null || value === undefined ? '' : formatAmount(value);
    }

    function renderProformaRows(rows, mode) {
        if (!rows || rows.length === 0) {
            return '<tr><td colspan="' + (mode === 'debitCredit' ? 3 : 2) +
                '" class="text-muted text-center">No data found.</td></tr>';
        }

        return rows.map(function (row) {
            var kind = row.kind || row.Kind || rowKind.account;
            var label = row.label || row.Label || '';
            var indent = row.indentLevel ?? row.IndentLevel ?? 0;
            var css = rowClass(kind) + ' ' + indentClass(indent);

            if (mode === 'debitCredit') {
                return '<tr class="' + css.trim() + '">' +
                    '<td class="col-label ' + indentClass(indent) + '">' + escapeHtml(label) + '</td>' +
                    '<td class="col-debit">' + renderAmountCell(row.debit ?? row.Debit) + '</td>' +
                    '<td class="col-credit">' + renderAmountCell(row.credit ?? row.Credit) + '</td>' +
                    '</tr>';
            }

            return '<tr class="' + css.trim() + '">' +
                '<td class="col-label ' + indentClass(indent) + '">' + escapeHtml(label) + '</td>' +
                '<td class="col-amount">' + renderAmountCell(row.amount ?? row.Amount) + '</td>' +
                '</tr>';
        }).join('');
    }

    function setCompanyHeader(company) {
        if (!company) {
            return;
        }

        $('#report-company-name').text(company.companyName || company.CompanyName || '');
    }

    function resetPrintFit() {
        var $content = $('#report-content');
        $content.removeClass('report-proforma-fit').css({
            transform: '',
            width: ''
        });
    }

    function applyPrintFit() {
        var $content = $('#report-content');
        resetPrintFit();

        if (!$content.hasClass('report-proforma-single-page')) {
            return;
        }

        var printableHeight = window.innerHeight || 1050;
        var contentHeight = $content[0].scrollHeight;
        if (contentHeight <= printableHeight) {
            return;
        }

        var scale = Math.max(0.55, printableHeight / contentHeight);
        $content.addClass('report-proforma-fit').css({
            transform: 'scale(' + scale.toFixed(3) + ')',
            width: ((1 / scale) * 100).toFixed(2) + '%'
        });
    }

    window.addEventListener('beforeprint', applyPrintFit);
    window.addEventListener('afterprint', resetPrintFit);

    window.FinancialReportCommon = {
        rowKind: rowKind,
        escapeHtml: escapeHtml,
        formatAmount: formatAmount,
        formatProformaDate: formatProformaDate,
        formatProformaDateRange: formatProformaDateRange,
        toInputDate: toInputDate,
        getApiErrorMessage: getApiErrorMessage,
        renderProformaRows: renderProformaRows,
        setCompanyHeader: setCompanyHeader,
        applyPrintFit: applyPrintFit,
        resetPrintFit: resetPrintFit
    };
})(window);
