(function () {
    'use strict';

    var common = window.FinancialReportCommon;

    function renderReport(data) {
        $('#report-period').text(common.formatProformaDateRange(data.fromDate, data.toDate));

        var rows = data.rows || data.Rows || [];
        $('#report-lines').html(common.renderProformaRows(rows, 'amount'));
        common.resetPrintFit();
    }

    function loadReport() {
        $.getJSON('/api/financial-reports/profit-and-loss', {
            fromDate: $('#filter-from').val(),
            toDate: $('#filter-to').val()
        })
            .done(renderReport)
            .fail(function (xhr) {
                alert(common.getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        var today = new Date();
        var monthStart = new Date(today.getFullYear(), today.getMonth(), 1);

        $('#filter-from').val(common.toInputDate(monthStart));
        $('#filter-to').val(common.toInputDate(today));

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
