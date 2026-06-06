(function () {
    'use strict';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatAmount(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatDate(value) {
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return value;
        }
        return d.toLocaleDateString('en-GB');
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

    function renderReport(data) {
        $('#report-period').text(
            'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) +
            ' — ' + data.accountCount + ' account(s)'
        );

        var $tbody = $('#report-lines');
        $tbody.empty();

        if (!data.lines || data.lines.length === 0) {
            $tbody.append('<tr><td colspan="8" class="text-muted text-center">No accounts with activity found.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        data.lines.forEach(function (line) {
            $tbody.append(
                '<tr>' +
                '<td><code>' + escapeHtml(line.accountNumber) + '</code></td>' +
                '<td>' + escapeHtml(line.accountName) + '</td>' +
                '<td>' + (line.typeName ? escapeHtml(line.typeName) : '—') + '</td>' +
                '<td class="text-end">' + formatAmount(line.openingBalance) + '</td>' +
                '<td class="text-end">' + formatAmount(line.periodDebit) + '</td>' +
                '<td class="text-end">' + formatAmount(line.periodCredit) + '</td>' +
                '<td class="text-end">' + formatAmount(line.closingDebit) + '</td>' +
                '<td class="text-end">' + formatAmount(line.closingCredit) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-debit').text(formatAmount(data.totalClosingDebit));
        $('#report-total-credit').text(formatAmount(data.totalClosingCredit));
        $('#report-footer').removeClass('d-none');
    }

    function loadReport() {
        $.getJSON('/api/financial-reports/trial-balance', {
            fromDate: $('#filter-from').val(),
            toDate: $('#filter-to').val()
        })
            .done(renderReport)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        var today = new Date();
        var monthStart = new Date(today.getFullYear(), today.getMonth(), 1);

        $('#filter-from').val(toInputDate(monthStart));
        $('#filter-to').val(toInputDate(today));

        $.getJSON('/api/company/current')
            .done(loadReport)
            .fail(function () {
                $('#report-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to run this report.');
            });

        $('#btn-load-report').on('click', loadReport);
        $('#btn-print-report').on('click', function () {
            window.print();
        });
    });
})();
