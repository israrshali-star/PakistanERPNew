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
            'As of: ' + formatDate(data.asOfDate) + ' — ' + data.customerCount + ' customer(s)'
        );

        var $tbody = $('#report-lines');
        $tbody.empty();

        if (!data.lines || data.lines.length === 0) {
            $tbody.append('<tr><td colspan="8" class="text-muted text-center">No outstanding receivables found.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        data.lines.forEach(function (line) {
            $tbody.append(
                '<tr>' +
                '<td><code>' + escapeHtml(line.customerCode) + '</code></td>' +
                '<td>' + escapeHtml(line.customerName) + '</td>' +
                '<td class="text-end">' + formatAmount(line.openingBalance) + '</td>' +
                '<td class="text-end">' + formatAmount(line.current) + '</td>' +
                '<td class="text-end">' + formatAmount(line.days31To60) + '</td>' +
                '<td class="text-end">' + formatAmount(line.days61To90) + '</td>' +
                '<td class="text-end">' + formatAmount(line.over90) + '</td>' +
                '<td class="text-end fw-semibold">' + formatAmount(line.total) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-opening').text(formatAmount(data.totalOpeningBalance));
        $('#report-total-current').text(formatAmount(data.totalCurrent));
        $('#report-total-31-60').text(formatAmount(data.totalDays31To60));
        $('#report-total-61-90').text(formatAmount(data.totalDays61To90));
        $('#report-total-over-90').text(formatAmount(data.totalOver90));
        $('#report-total-grand').text(formatAmount(data.grandTotal));
        $('#report-footer').removeClass('d-none');
    }

    function loadReport() {
        $.getJSON('/api/financial-reports/ar-aging-summary', {
            asOfDate: $('#filter-as-of').val()
        })
            .done(renderReport)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        var today = new Date();

        $('#filter-as-of').val(toInputDate(today));

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
