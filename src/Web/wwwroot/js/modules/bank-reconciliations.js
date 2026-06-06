(function () {
    'use strict';

    var historyTable = null;
    var canEdit = false;
    var preview = null;

    function formatMoney(value) {
        return (parseFloat(value) || 0).toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatDate(value) {
        var d = new Date(value);
        return Number.isNaN(d.getTime()) ? value : d.toLocaleDateString('en-GB');
    }

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function showError(message) {
        $('#recon-form-success').addClass('d-none');
        $('#recon-form-error').removeClass('d-none').text(message);
    }

    function showSuccess(message) {
        $('#recon-form-error').addClass('d-none');
        $('#recon-form-success').removeClass('d-none').text(message);
    }

    function loadBanks() {
        return $.getJSON('/api/bank-reconciliations/banks').done(function (banks) {
            var $select = $('#recon-bank-id');
            $select.find('option:not(:first)').remove();
            (banks || []).forEach(function (b) {
                $select.append($('<option></option>').val(b.id).text(b.bankName + ' (' + b.accountNumber + ')'));
            });
        });
    }

    function renderUnreconciled(data) {
        preview = data;
        $('#book-balance').text(formatMoney(data.bookBalance));
        $('#unreconciled-count').text(data.unreconciledCount);

        var $tbody = $('#unreconciled-body');
        $tbody.empty();

        if (!data.unreconciledTransactions || data.unreconciledTransactions.length === 0) {
            $tbody.append('<tr><td colspan="5" class="text-muted text-center">No unreconciled transactions.</td></tr>');
            return;
        }

        data.unreconciledTransactions.forEach(function (txn) {
            $tbody.append(
                '<tr>' +
                '<td><input type="checkbox" class="txn-select" value="' + txn.id + '" checked /></td>' +
                '<td>' + formatDate(txn.transactionDate) + '</td>' +
                '<td>' + escapeHtml(txn.transactionType) + '</td>' +
                '<td class="text-end">' + formatMoney(txn.amount) + '</td>' +
                '<td>' + (txn.description ? escapeHtml(txn.description) : '—') + '</td>' +
                '</tr>'
            );
        });
    }

    function loadPreview(bankId) {
        if (!bankId) {
            $('#book-balance, #unreconciled-count').text('—');
            $('#unreconciled-body').html('<tr><td colspan="5" class="text-muted text-center">Select a bank account</td></tr>');
            preview = null;
            return;
        }

        $.getJSON('/api/bank-reconciliations/preview/' + bankId)
            .done(renderUnreconciled)
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Failed to load preview.'));
            });
    }

    function initHistoryTable() {
        if (historyTable) {
            historyTable.ajax.reload();
            return;
        }

        historyTable = $('#recon-history-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: { url: '/api/bank-reconciliations/datatable' },
            order: [[1, 'desc']],
            pageLength: 10,
            columns: [
                { data: 'bankName' },
                { data: 'statementDate', render: formatDate },
                { data: 'statementBalance', className: 'text-end text-currency', render: formatMoney },
                { data: 'bookBalance', className: 'text-end text-currency', render: formatMoney },
                {
                    data: 'difference',
                    className: 'text-end text-currency',
                    render: function (d) {
                        var num = parseFloat(d) || 0;
                        var cls = num === 0 ? '' : ' text-warning';
                        return '<span class="' + cls + '">' + formatMoney(num) + '</span>';
                    }
                }
            ],
            language: { emptyTable: 'No reconciliation history yet.' }
        });
    }

    function completeReconciliation() {
        if (!canEdit) return;

        var bankId = parseInt($('#recon-bank-id').val(), 10) || 0;
        var statementDate = $('#statement-date').val();
        var statementBalance = parseFloat($('#statement-balance').val());

        if (!bankId) {
            showError('Select a bank account.');
            return;
        }
        if (!statementDate) {
            showError('Statement date is required.');
            return;
        }
        if (Number.isNaN(statementBalance)) {
            showError('Statement balance is required.');
            return;
        }

        var transactionIds = [];
        $('.txn-select:checked').each(function () {
            transactionIds.push(parseInt($(this).val(), 10));
        });

        if (transactionIds.length === 0) {
            showError('Select at least one transaction to reconcile.');
            return;
        }

        if (!confirm('Mark ' + transactionIds.length + ' transaction(s) as reconciled?')) {
            return;
        }

        $.ajax({
            url: '/api/bank-reconciliations/complete',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                bankId: bankId,
                statementDate: statementDate,
                statementBalance: statementBalance,
                transactionIds: transactionIds
            })
        })
            .done(function (result) {
                showSuccess(result.message || 'Reconciliation completed.');
                loadPreview(bankId);
                if (historyTable) historyTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Reconciliation failed.'));
            });
    }

    $(function () {
        canEdit = $('#recon-permissions').data('can-edit') === true;
        if (!canEdit) $('#btn-complete-recon').remove();

        $('#recon-bank-id').select2({ theme: 'bootstrap-5', width: '100%' });
        $('#statement-date').val(new Date().toISOString().slice(0, 10));

        $.getJSON('/api/company/current')
            .done(function () {
                $('#recon-company-warning').addClass('d-none');
                loadBanks();
                initHistoryTable();
            })
            .fail(function () {
                $('#recon-company-warning').removeClass('d-none').text('Select a company from the top navbar.');
            });

        $('#recon-bank-id').on('change', function () {
            loadPreview(parseInt($(this).val(), 10) || 0);
        });

        $('#select-all-txn').on('change', function () {
            $('.txn-select').prop('checked', $(this).is(':checked'));
        });

        $('#btn-complete-recon').on('click', completeReconciliation);
    });
})();
