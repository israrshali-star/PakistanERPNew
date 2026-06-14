(function () {
    'use strict';

    function formatDate(value) {
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return value;
        }
        var day = String(d.getDate()).padStart(2, '0');
        var month = String(d.getMonth() + 1).padStart(2, '0');
        var year = d.getFullYear();
        return day + '/' + month + '/' + year;
    }

    function parseDateInput(value) {
        if (!value) {
            return null;
        }
        var parts = value.split('/');
        if (parts.length !== 3) {
            return null;
        }
        return parts[2] + '-' + parts[1] + '-' + parts[0];
    }

    function formatAmount(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function renderStatement(data) {
        $('#stmt-vendor-name').text(data.vendor.vendorName);
        $('#stmt-vendor-code').text(data.vendor.vendorCode);

        if (data.vendor.ntn) {
            $('#stmt-ntn').text(data.vendor.ntn);
            $('#stmt-ntn-wrap').removeClass('d-none');
        } else {
            $('#stmt-ntn-wrap').addClass('d-none');
        }

        $('#stmt-period').text(
            'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate)
        );

        var $tbody = $('#statement-entries');
        $tbody.empty();

        if (!data.entries || data.entries.length === 0) {
            $tbody.append('<tr><td colspan="6" class="text-muted text-center">No transactions in this period.</td></tr>');
            $('#statement-footer').addClass('d-none');
            return;
        }

        data.entries.forEach(function (entry) {
            var dateText = entry.date && entry.date.indexOf('0001') === -1
                ? formatDate(entry.date)
                : '—';

            $tbody.append(
                '<tr>' +
                '<td>' + dateText + '</td>' +
                '<td><code>' + $('<div>').text(entry.reference).html() + '</code></td>' +
                '<td>' + $('<div>').text(entry.description).html() + '</td>' +
                '<td class="text-end">' + (entry.debit > 0 ? formatAmount(entry.debit) : '—') + '</td>' +
                '<td class="text-end">' + (entry.credit > 0 ? formatAmount(entry.credit) : '—') + '</td>' +
                '<td class="text-end fw-semibold">' + formatAmount(entry.balance) + '</td>' +
                '</tr>'
            );
        });

        $('#stmt-closing-balance').text(formatAmount(data.closingBalance));
        $('#statement-footer').removeClass('d-none');
    }

    function loadStatement() {
        var vendorId = $('#statement-vendor-id').val();
        var from = parseDateInput($('#statement-from').val());
        var to = parseDateInput($('#statement-to').val());

        if (!from || !to) {
            alert('Please select valid from and to dates.');
            return;
        }

        $.getJSON('/api/vendors/' + vendorId + '/statement', { from: from, to: to })
            .done(renderStatement)
            .fail(function (xhr) {
                var message = 'Failed to load statement.';
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    message = xhr.responseJSON.message;
                }
                alert(message);
            });
    }

    $(function () {
        var today = new Date();
        var firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);

        if (typeof flatpickr !== 'undefined') {
            flatpickr('#statement-from', {
                dateFormat: 'd/m/Y',
                defaultDate: firstOfMonth,
                allowInput: true
            });
            flatpickr('#statement-to', {
                dateFormat: 'd/m/Y',
                defaultDate: today,
                allowInput: true
            });
        }

        $('#btn-load-statement').on('click', loadStatement);
        $('#btn-print-statement').on('click', function () {
            window.print();
        });

        $('#btn-share-statement').on('click', function () {
            var from = parseDateInput($('#statement-from').val());
            var to = parseDateInput($('#statement-to').val());
            if (!from || !to) {
                alert('Please select valid from and to dates.');
                return;
            }
            if (window.LedgerShare) {
                window.LedgerShare.open({
                    partyType: 'vendor',
                    partyId: parseInt($('#statement-vendor-id').val(), 10),
                    fromDate: from,
                    toDate: to
                });
            }
        });

        loadStatement();
    });
})();
