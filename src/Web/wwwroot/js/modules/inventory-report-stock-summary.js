(function () {

    'use strict';



    function escapeHtml(text) {

        return $('<div>').text(text ?? '').html();

    }



    function formatAmount(value) {

        var num = parseFloat(value) || 0;

        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    }



    function formatQty(value, unitSymbol) {

        var num = parseFloat(value) || 0;

        var isCarton = /^ctn$/i.test(unitSymbol || '');

        return num.toLocaleString('en-PK', {

            minimumFractionDigits: isCarton ? 0 : 2,

            maximumFractionDigits: isCarton ? 0 : 2

        });

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



    function endOfMonth(date) {

        return new Date(date.getFullYear(), date.getMonth() + 1, 0);

    }



    function endOfQuarter(date) {

        var quarter = Math.floor(date.getMonth() / 3);

        return new Date(date.getFullYear(), (quarter + 1) * 3, 0);

    }



    function endOfFiscalYear(date) {

        return new Date(date.getFullYear(), 5, 30);

    }



    function resolvePresetDate(preset) {

        var today = new Date();

        today.setHours(0, 0, 0, 0);



        switch (preset) {

            case 'today':

                return today;

            case 'this-month':

                return endOfMonth(today);

            case 'last-month':

                return endOfMonth(new Date(today.getFullYear(), today.getMonth() - 1, 1));

            case 'this-quarter':

                return endOfQuarter(today);

            case 'last-quarter':

                return endOfQuarter(new Date(today.getFullYear(), today.getMonth() - 3, 1));

            case 'this-year':

                return today.getMonth() >= 6

                    ? endOfFiscalYear(new Date(today.getFullYear() + 1, 0, 1))

                    : endOfFiscalYear(today);

            case 'last-year':

                return today.getMonth() >= 6

                    ? endOfFiscalYear(today)

                    : endOfFiscalYear(new Date(today.getFullYear() - 1, 0, 1));

            default:

                return null;

        }

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

            'As of: ' + formatDate(data.asOfDate) + ' — ' + data.itemCount + ' item(s)'

        );



        var $tbody = $('#report-lines');

        $tbody.empty();



        if (!data.lines || data.lines.length === 0) {

            $tbody.append('<tr><td colspan="10" class="text-muted text-center">No items found.</td></tr>');

            $('#report-footer').addClass('d-none');

            return;

        }



        data.lines.forEach(function (line) {

            $tbody.append(

                '<tr>' +

                '<td><code>' + escapeHtml(line.itemCode) + '</code></td>' +

                '<td>' + escapeHtml(line.itemName) + '</td>' +

                '<td>' + (line.categoryName ? escapeHtml(line.categoryName) : '—') + '</td>' +

                '<td>' + escapeHtml(line.unitSymbol) + '</td>' +

                '<td class="text-end">' + formatQty(line.currentStock, line.unitSymbol) + '</td>' +

                '<td class="text-end">' + formatQty(line.currentCartons, 'Ctn') + '</td>' +

                '<td class="text-end">' + formatQty(line.minimumStock, line.unitSymbol) + '</td>' +

                '<td class="text-end">' + formatQty(line.reorderLevel, line.unitSymbol) + '</td>' +

                '<td class="text-end">' + formatAmount(line.purchaseRate) + '</td>' +

                '<td class="text-end fw-semibold">' + formatAmount(line.stockValue) + '</td>' +

                '</tr>'

            );

        });



        $('#report-total-stock').text(formatQty(data.totalStock, 'Kg'));

        $('#report-total-cartons').text(formatQty(data.totalCartons, 'Ctn'));

        $('#report-total-value').text(formatAmount(data.totalStockValue));

        $('#report-footer').removeClass('d-none');

    }



    function loadCategories() {

        return $.getJSON('/api/inventory-reports/categories').done(function (categories) {

            var $select = $('#filter-category');

            (categories || []).forEach(function (c) {

                $select.append($('<option></option>').val(c.id).text(c.name));

            });

        });

    }



    function applyDatePreset() {

        var preset = $('#filter-date-preset').val();

        if (preset === 'custom') {

            return;

        }



        var resolved = resolvePresetDate(preset);

        if (resolved) {

            $('#filter-as-of').val(toInputDate(resolved));

        }

    }



    function loadReport() {

        var categoryId = $('#filter-category').val();

        var activeOnly = $('#filter-active-only').is(':checked');

        var hideZeroQoh = $('#filter-hide-zero-qoh').is(':checked');

        var asOfDate = $('#filter-as-of').val();

        var params = {

            activeOnly: activeOnly,

            hideZeroQoh: hideZeroQoh,

            asOfDate: asOfDate

        };



        if (categoryId) {

            params.categoryId = categoryId;

        }



        $.getJSON('/api/inventory-reports/stock-summary', params)

            .done(renderReport)

            .fail(function (xhr) {

                alert(getApiErrorMessage(xhr, 'Failed to load report.'));

            });

    }



    $(function () {

        var today = new Date();



        $('#filter-as-of').val(toInputDate(today));



        $('#filter-date-preset').on('change', function () {

            applyDatePreset();

        });



        $('#filter-as-of').on('change', function () {

            $('#filter-date-preset').val('custom');

        });



        $.getJSON('/api/company/current')

            .done(function () {

                loadCategories().always(loadReport);

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


