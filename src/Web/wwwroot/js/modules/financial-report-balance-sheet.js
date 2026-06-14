(function () {
    'use strict';

    var common = window.FinancialReportCommon;

    function renderReport(data) {
        $('#report-period').text('As of ' + common.formatProformaDate(data.asOfDate));

        var rows = data.rows || data.Rows || [];
        $('#report-lines').html(common.renderProformaRows(rows, 'amount'));
        common.resetPrintFit();
    }

    function loadReport() {
        $.getJSON('/api/financial-reports/balance-sheet', {
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
