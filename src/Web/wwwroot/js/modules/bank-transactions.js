(function () {
    'use strict';

    var bankTxnModal = null;
    var dataTable = null;
    var canCreate = false;
    var banks = [];
    var filterBankId = '';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatMoney(value) {
        return (parseFloat(value) || 0).toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatDate(value) {
        var d = new Date(value);
        return Number.isNaN(d.getTime()) ? value : d.toLocaleDateString('en-GB');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function showFormError(message) {
        $('#bank-txn-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#bank-txn-form-error').addClass('d-none').text('');
    }

    function toggleTransferFields() {
        var isTransfer = parseInt($('#txn-type').val(), 10) === 3;
        $('.transfer-fields').toggleClass('d-none', !isTransfer);
    }

    function updateBankBalanceHint() {
        var bankId = parseInt($('#txn-bank-id').val(), 10) || 0;
        var bank = banks.find(function (b) { return b.id === bankId; });
        $('#txn-bank-balance').text(bank ? 'Available balance: PKR ' + formatMoney(bank.currentBalance) : '');
    }

    function buildBankOptions($select, excludeId) {
        $select.find('option:not(:first)').remove();
        banks.forEach(function (b) {
            if (excludeId && b.id === excludeId) return;
            $select.append($('<option></option>').val(b.id).text(b.bankName + ' (' + b.accountNumber + ')'));
        });
    }

    function loadBanks() {
        return $.getJSON('/api/bank-transactions/banks').done(function (res) {
            banks = res || [];
            buildBankOptions($('#filter-bank-id'));
            buildBankOptions($('#txn-bank-id'));
            buildBankOptions($('#txn-transfer-to-id'));
        });
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#bank-transactions-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/bank-transactions/datatable',
                data: function (d) {
                    if (filterBankId) d.bankId = filterBankId;
                },
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#bank-txn-company-warning').removeClass('d-none').text(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[1, 'desc']],
            pageLength: 25,
            columns: [
                { data: 'bankName' },
                { data: 'transactionDate', render: formatDate },
                { data: 'transactionType' },
                { data: 'transferToBankName', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } },
                { data: 'amount', className: 'text-end text-currency', render: formatMoney },
                { data: 'description', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } },
                {
                    data: 'isReconciled',
                    render: function (d) {
                        return d ? '<span class="badge bg-success">Yes</span>' : '<span class="badge bg-secondary">No</span>';
                    }
                }
            ],
            language: { emptyTable: 'No bank transactions yet.' }
        });
    }

    function openCreateModal() {
        if (banks.length === 0) {
            alert('Add a bank account first under Banking → Bank Accounts.');
            return;
        }
        clearFormError();
        $('#bank-txn-form')[0].reset();
        $('#txn-date').val(new Date().toISOString().slice(0, 10));
        $('#txn-type').val('1');
        toggleTransferFields();
        updateBankBalanceHint();
        bankTxnModal.show();
    }

    function saveTransaction(e) {
        e.preventDefault();
        clearFormError();

        var transferId = parseInt($('#txn-transfer-to-id').val(), 10) || 0;
        var chequeDate = $('#txn-cheque-date').val();
        var payload = {
            bankId: parseInt($('#txn-bank-id').val(), 10) || 0,
            transactionType: parseInt($('#txn-type').val(), 10) || 1,
            transferToBankId: transferId > 0 ? transferId : null,
            transactionDate: $('#txn-date').val(),
            chequeNumber: $('#txn-cheque-number').val().trim() || null,
            chequeDate: chequeDate || null,
            amount: parseFloat($('#txn-amount').val()) || 0,
            description: $('#txn-description').val().trim() || null
        };

        $.ajax({
            url: '/api/bank-transactions',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                bankTxnModal.hide();
                loadBanks().always(function () {
                    dataTable.ajax.reload(null, false);
                });
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save transaction.'));
            });
    }

    $(function () {
        canCreate = $('#bank-txn-permissions').data('can-create') === true;
        if (!canCreate) $('#btn-add-bank-txn').remove();

        bankTxnModal = new bootstrap.Modal(document.getElementById('bankTxnModal'));
        $('#txn-bank-id, #txn-transfer-to-id').select2({ theme: 'bootstrap-5', width: '100%', dropdownParent: $('#bankTxnModal') });

        $.getJSON('/api/company/current')
            .done(function () {
                $('#bank-txn-company-warning').addClass('d-none');
                loadBanks().always(initDataTable);
            })
            .fail(function () {
                $('#bank-txn-company-warning').removeClass('d-none').text('Select a company from the top navbar.');
            });

        $('#btn-add-bank-txn').on('click', openCreateModal);
        $('#txn-type').on('change', toggleTransferFields);
        $('#txn-bank-id').on('change', updateBankBalanceHint);
        $('#bank-txn-form').on('submit', saveTransaction);
        $('#btn-apply-filter').on('click', function () {
            filterBankId = $('#filter-bank-id').val() || '';
            if (dataTable) dataTable.ajax.reload();
        });
    });
})();
