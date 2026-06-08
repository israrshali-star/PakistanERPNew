(function () {
    'use strict';

    var sources = [];
    var currentSource = null;
    var lastRequest = null;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatValue(value, dataType) {
        if (value === null || value === undefined || value === '') {
            return '—';
        }

        if (dataType === 'decimal' || dataType === 'number') {
            var num = parseFloat(value) || 0;
            return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        if (dataType === 'date' || dataType === 'datetime') {
            var d = new Date(value);
            if (Number.isNaN(d.getTime())) {
                return escapeHtml(value);
            }
            return dataType === 'date'
                ? d.toLocaleDateString('en-GB')
                : d.toLocaleString('en-GB');
        }

        if (dataType === 'boolean') {
            return value === true || value === 'True' || value === 'true' ? 'Yes' : 'No';
        }

        return escapeHtml(value);
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

    function renderColumnPicker(source) {
        var $list = $('#column-list');
        $list.empty();

        if (!source || !source.columns || source.columns.length === 0) {
            $list.addClass('text-muted').text('No columns available.');
            $('#btn-select-all-columns, #btn-clear-columns').prop('disabled', true);
            return;
        }

        $list.removeClass('text-muted').addClass('row g-2');

        source.columns.forEach(function (col) {
            var id = 'col-' + col.key;
            $list.append(
                '<div class="col-md-4 col-lg-3">' +
                '<div class="form-check">' +
                '<input class="form-check-input column-check" type="checkbox" value="' + escapeHtml(col.key) + '" id="' + id + '" checked />' +
                '<label class="form-check-label small" for="' + id + '">' + escapeHtml(col.label) + '</label>' +
                '</div></div>'
            );
        });

        $('#btn-select-all-columns, #btn-clear-columns').prop('disabled', false);
    }

    function getSelectedColumns() {
        return $('.column-check:checked').map(function () {
            return $(this).val();
        }).get();
    }

    function buildRequest() {
        return {
            sourceKey: $('#source-key').val(),
            columns: getSelectedColumns(),
            fromDate: $('#filter-from').val() || null,
            toDate: $('#filter-to').val() || null,
            maxRows: 5000
        };
    }

    function renderReport(result) {
        $('#report-title').text(result.sourceName || 'Custom Report');
        var meta = result.totalRows + ' row(s)';
        if (result.truncated) {
            meta += ' — showing first 5,000 rows';
        }
        $('#report-meta').text(meta);

        var $head = $('#report-head');
        var $body = $('#report-body');
        $head.empty();
        $body.empty();

        if (!result.columns || result.columns.length === 0) {
            $body.append('<tr><td class="text-muted text-center">No columns selected.</td></tr>');
            $('#btn-export-excel').prop('disabled', true);
            return;
        }

        var headHtml = '<tr>';
        result.columns.forEach(function (col) {
            headHtml += '<th>' + escapeHtml(col.label) + '</th>';
        });
        headHtml += '</tr>';
        $head.html(headHtml);

        if (!result.rows || result.rows.length === 0) {
            $body.append('<tr><td colspan="' + result.columns.length + '" class="text-muted text-center">No rows found.</td></tr>');
            $('#btn-export-excel').prop('disabled', true);
            return;
        }

        result.rows.forEach(function (row) {
            var tr = '<tr>';
            result.columns.forEach(function (col) {
                var align = (col.dataType === 'decimal' || col.dataType === 'number') ? ' text-end' : '';
                tr += '<td class="' + align.trim() + '">' + formatValue(row[col.key], col.dataType) + '</td>';
            });
            tr += '</tr>';
            $body.append(tr);
        });

        $('#btn-export-excel').prop('disabled', false);
    }

    function onSourceChanged() {
        var key = $('#source-key').val();
        currentSource = sources.find(function (s) { return s.key === key; }) || null;

        if (!currentSource) {
            $('#source-description').text('');
            $('#date-filter-wrap, #date-filter-wrap-to').hide();
            renderColumnPicker(null);
            $('#btn-run-report').prop('disabled', true);
            return;
        }

        $('#source-description').text(currentSource.description || '');
        var showDates = !!currentSource.supportsDateFilter;
        $('#date-filter-wrap, #date-filter-wrap-to').toggle(showDates);
        renderColumnPicker(currentSource);
        $('#btn-run-report').prop('disabled', false);
    }

    function loadSources() {
        return $.getJSON('/api/custom-reports/sources').done(function (data) {
            sources = data || [];
            var $select = $('#source-key');
            sources.forEach(function (source) {
                $select.append($('<option></option>').val(source.key).text(source.name));
            });
        });
    }

    function runReport() {
        var request = buildRequest();
        if (!request.sourceKey) {
            window.showToast('Select a data source.', 'warning');
            return;
        }

        if (!request.columns.length) {
            window.showToast('Select at least one column.', 'warning');
            return;
        }

        lastRequest = request;
        $('#btn-run-report').prop('disabled', true).html('<span class="spinner-border spinner-border-sm me-1"></span>Running...');

        $.ajax({
            url: '/api/custom-reports/run',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(request)
        }).done(function (result) {
            renderReport(result);
        }).fail(function (xhr) {
            window.showToast(getApiErrorMessage(xhr, 'Could not run report.'), 'danger');
        }).always(function () {
            $('#btn-run-report').prop('disabled', false).html('<i class="fa-solid fa-play me-1"></i>Run Report');
        });
    }

    function exportExcel() {
        if (!lastRequest) {
            window.showToast('Run the report first.', 'warning');
            return;
        }

        $('#btn-export-excel').prop('disabled', true);

        fetch('/api/custom-reports/export', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(lastRequest)
        }).then(function (response) {
            if (!response.ok) {
                return response.json().then(function (body) {
                    throw new Error(body.message || 'Export failed.');
                });
            }
            return response.blob().then(function (blob) {
                var url = window.URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = 'CustomReport_' + lastRequest.sourceKey + '.xlsx';
                document.body.appendChild(a);
                a.click();
                a.remove();
                window.URL.revokeObjectURL(url);
            });
        }).catch(function (err) {
            window.showToast(err.message || 'Export failed.', 'danger');
        }).finally(function () {
            $('#btn-export-excel').prop('disabled', false);
        });
    }

    $(function () {
        var today = new Date();
        var monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
        $('#filter-from').val(toInputDate(monthStart));
        $('#filter-to').val(toInputDate(today));

        $.getJSON('/api/company/current').fail(function () {
            $('#report-company-warning')
                .removeClass('d-none')
                .text('Select a company from the top navbar before running custom reports.');
        });

        loadSources().fail(function () {
            window.showToast('Could not load report sources.', 'danger');
        });

        $('#source-key').on('change', onSourceChanged);
        $('#btn-run-report').on('click', runReport);
        $('#btn-export-excel').on('click', exportExcel);
        $('#btn-select-all-columns').on('click', function () {
            $('.column-check').prop('checked', true);
        });
        $('#btn-clear-columns').on('click', function () {
            $('.column-check').prop('checked', false);
        });
        $('#btn-print-report').on('click', function () {
            window.print();
        });
    });
})();
