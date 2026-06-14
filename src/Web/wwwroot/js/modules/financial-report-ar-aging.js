(function () {
    'use strict';

    var common = window.FinancialReportCommon;

    function renderReport(data) {
        $('#report-period').text(
            'As of ' + common.formatProformaDate(data.asOfDate) + ' — ' + data.customerCount + ' customer(s)'
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
                '<td><code>' + common.escapeHtml(line.customerCode) + '</code></td>' +
                '<td>' + common.escapeHtml(line.customerName) + '</td>' +
                '<td class="text-end">' + common.formatAmount(line.openingBalance) + '</td>' +
                '<td class="text-end">' + common.formatAmount(line.current) + '</td>' +
                '<td class="text-end">' + common.formatAmount(line.days31To60) + '</td>' +
                '<td class="text-end">' + common.formatAmount(line.days61To90) + '</td>' +
                '<td class="text-end">' + common.formatAmount(line.over90) + '</td>' +
                '<td class="text-end fw-semibold">' + common.formatAmount(line.total) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-opening').text(common.formatAmount(data.totalOpeningBalance));
        $('#report-total-current').text(common.formatAmount(data.totalCurrent));
        $('#report-total-31-60').text(common.formatAmount(data.totalDays31To60));
        $('#report-total-61-90').text(common.formatAmount(data.totalDays61To90));
        $('#report-total-over-90').text(common.formatAmount(data.totalOver90));
        $('#report-total-grand').text(common.formatAmount(data.grandTotal));
        $('#report-footer').removeClass('d-none');
    }

    function loadReport() {
        $.getJSON('/api/financial-reports/ar-aging-summary', {
            asOfDate: $('#filter-as-of').val()
        })
            .done(renderReport)
            .fail(function (xhr) {
                alert(common.getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        var today = new Date();

        $('#filter-as-of').val(common.toInputDate(today));

        $.getJSON('/api/company/current')
            .done(function (company) {
                common.setCompanyHeader(company);
                loadReport();
            })
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
