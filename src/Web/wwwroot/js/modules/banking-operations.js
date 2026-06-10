(function () {
    'use strict';

    var dataTable = null;
    var canCreate = false;
    var transactionType = 1;
    var bankAccounts = [];
    var transferAccounts = [];
    var counterAccounts = [];
    var undepositedCheques = [];

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
        if (body && body.message) return body.message;
        if (body && body.Message) return body.Message;
        return fallback;
    }

    function showFormError(message) {
        $('#bank-op-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#bank-op-form-error').addClass('d-none').text('');
    }

    function findAccount(list, id) {
        return list.find(function (a) { return a.id === id; });
    }

    function buildOptions($select, list, excludeId) {
        $select.find('option:not(:first)').remove();
        list.forEach(function (a) {
            if (excludeId && a.id === excludeId) return;
            $select.append($('<option></option>').val(a.id).text(a.label || (a.accountNumber + ' — ' + a.accountName)));
        });
    }

    function updateBalanceHint($select, $hint, list) {
        var id = parseInt($select.val(), 10) || 0;
        var account = findAccount(list, id);
        $hint.text(account ? 'GL balance: PKR ' + formatMoney(account.balance) : '');
    }

    function updateChequeNumberHint(res) {
        var $hint = $('#op-cheque-number-hint');
        if (!$hint.length) return;

        if (res && res.isConfigured) {
            $hint.text('Next cheque # after posting: auto-increment from saved sequence.');
        } else {
            $hint.text('Enter a starting cheque # — it will auto-increment after each posted cheque.');
        }
    }

    function loadNextChequeNumber(coaId) {
        if (transactionType !== 2 || !coaId) {
            $('#op-cheque-number').val('');
            updateChequeNumberHint(null);
            return $.when();
        }

        return $.getJSON('/api/bank-transactions/next-cheque-number', { chartOfAccountId: coaId })
            .done(function (res) {
                if (res && res.nextChequeNumber) {
                    $('#op-cheque-number').val(res.nextChequeNumber);
                } else {
                    $('#op-cheque-number').val('');
                }
                updateChequeNumberHint(res);
            })
            .fail(function () {
                updateChequeNumberHint(null);
            });
    }

    function saveStartingChequeNumber() {
        if (!canCreate || transactionType !== 2) return;

        var coaId = parseInt($('#op-bank-account-id').val(), 10) || 0;
        var chequeNumber = $('#op-cheque-number').val()?.trim() || '';
        if (!coaId) {
            showFormError('Select a bank account first.');
            return;
        }
        if (!chequeNumber) {
            showFormError('Enter a starting cheque number.');
            return;
        }

        clearFormError();
        $.ajax({
            url: '/api/bank-transactions/next-cheque-number',
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify({
                chartOfAccountId: coaId,
                nextChequeNumber: chequeNumber
            })
        })
            .done(function (res) {
                updateChequeNumberHint(res && res.nextChequeNumber ? res.nextChequeNumber : { isConfigured: true });
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save starting cheque number.'));
            });
    }

    function loadUndepositedSummary() {
        if (transactionType !== 1) return $.when();
        return $.getJSON('/api/bank-transactions/undeposited-summary').done(function (res) {
            $('#undeposited-balance').text(formatMoney(res.balance));
            if (res.accountNumber) $('#undeposited-account-number').text(res.accountNumber);
        });
    }

    function renderUndepositedCheques() {
        var $body = $('#undeposited-cheques-body');
        if (!$body.length) return;

        $body.empty();
        if (!undepositedCheques.length) {
            $body.append(
                '<tr><td colspan="6" class="text-center text-muted py-3">No undeposited cheques.</td></tr>'
            );
            updateDepositSelectedTotal();
            return;
        }

        undepositedCheques.forEach(function (cheque) {
            var chequeNumber = cheque.chequeNumber ? escapeHtml(cheque.chequeNumber) : '—';
            $body.append(
                '<tr>' +
                '<td><input type="checkbox" class="form-check-input deposit-cheque-checkbox" value="' + cheque.id + '" data-amount="' + cheque.amount + '" /></td>' +
                '<td>' + escapeHtml(cheque.customerName) + '</td>' +
                '<td>' + escapeHtml(cheque.receiptNumber) + '</td>' +
                '<td>' + chequeNumber + '</td>' +
                '<td>' + formatDate(cheque.receiptDate) + '</td>' +
                '<td class="text-end text-currency">' + formatMoney(cheque.amount) + '</td>' +
                '</tr>'
            );
        });
        updateDepositSelectedTotal();
    }

    function updateDepositSelectedTotal() {
        var total = 0;
        $('.deposit-cheque-checkbox:checked').each(function () {
            total += parseFloat($(this).data('amount')) || 0;
        });
        $('#deposit-selected-total').text(formatMoney(total));
        $('#deposit-select-all').prop(
            'checked',
            $('.deposit-cheque-checkbox').length > 0 &&
            $('.deposit-cheque-checkbox:checked').length === $('.deposit-cheque-checkbox').length
        );
    }

    function getSelectedDepositReceiptIds() {
        return $('.deposit-cheque-checkbox:checked').map(function () {
            return parseInt($(this).val(), 10);
        }).get().filter(function (id) { return id > 0; });
    }

    function loadUndepositedCheques() {
        if (transactionType !== 1) return $.when();
        return $.getJSON('/api/bank-transactions/undeposited-cheques')
            .done(function (res) {
                undepositedCheques = res || [];
                renderUndepositedCheques();
            })
            .fail(function () {
                undepositedCheques = [];
                renderUndepositedCheques();
            });
    }

    function loadLookups() {
        var requests = [];
        if (transactionType === 3) {
            requests.push($.getJSON('/api/bank-transactions/coa-transfer').done(function (res) {
                transferAccounts = res || [];
                buildOptions($('#op-from-account-id'), transferAccounts);
                buildOptions($('#op-to-account-id'), transferAccounts);
            }));
        } else {
            requests.push($.getJSON('/api/bank-transactions/coa-banks').done(function (res) {
                bankAccounts = res || [];
                buildOptions($('#op-bank-account-id'), bankAccounts);
            }));
        }

        if (transactionType === 2) {
            requests.push($.getJSON('/api/bank-transactions/coa-counter').done(function (res) {
                counterAccounts = res || [];
                buildOptions($('#op-counter-account-id'), counterAccounts);
            }));
        }

        requests.push(loadUndepositedSummary());
        if (transactionType === 1) {
            requests.push(loadUndepositedCheques());
        }
        return $.when.apply($, requests);
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        var columns = [
            { data: 'accountLabel' },
            { data: 'transactionDate', render: formatDate }
        ];

        if (transactionType === 3) {
            columns.push({ data: 'transferToAccountLabel', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } });
        } else if (transactionType === 2) {
            columns.push({ data: 'partyName', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } });
        }

        columns.push(
            { data: 'amount', className: 'text-end text-currency', render: formatMoney },
            { data: 'description', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } }
        );

        dataTable = $('#bank-op-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/bank-transactions/datatable',
                data: function (d) {
                    d.transactionType = transactionType;
                },
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#bank-op-company-warning').removeClass('d-none').text(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[1, 'desc']],
            pageLength: 10,
            columns: columns,
            language: { emptyTable: 'No transactions yet.' }
        });
    }

    function saveOperation(e) {
        e.preventDefault();
        if (!canCreate) return;
        clearFormError();

        var chequeDate = $('#op-cheque-date').val();
        var payload = {
            transactionType: transactionType,
            transactionDate: $('#op-date').val(),
            amount: parseFloat($('#op-amount').val()) || 0,
            description: $('#op-description').val().trim() || null,
            chequeNumber: $('#op-cheque-number').val()?.trim() || null,
            chequeDate: chequeDate || null,
            partyName: $('#op-party-name').val()?.trim() || null,
            counterChartOfAccountId: parseInt($('#op-counter-account-id').val(), 10) || null
        };

        if (transactionType === 1) {
            payload.customerReceiptIds = getSelectedDepositReceiptIds();
            if (!payload.customerReceiptIds.length) {
                showFormError('Select at least one cheque to deposit.');
                return;
            }
        }

        if (transactionType === 3) {
            payload.chartOfAccountId = parseInt($('#op-from-account-id').val(), 10) || 0;
            payload.transferToChartOfAccountId = parseInt($('#op-to-account-id').val(), 10) || null;
        } else {
            payload.chartOfAccountId = parseInt($('#op-bank-account-id').val(), 10) || 0;
        }

        $.ajax({
            url: '/api/bank-transactions',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                var selectedBankId = parseInt($('#op-bank-account-id').val(), 10) || 0;
                $('#bank-op-form')[0].reset();
                $('#op-date').val(new Date().toISOString().slice(0, 10));
                loadLookups().always(function () {
                    if (transactionType === 2 && selectedBankId) {
                        $('#op-bank-account-id').val(String(selectedBankId)).trigger('change');
                    }
                    dataTable.ajax.reload(null, false);
                });
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not post transaction.'));
            });
    }

    $(function () {
        canCreate = $('#bank-op-permissions').data('can-create') === true;
        transactionType = parseInt($('#bank-op-permissions').data('transaction-type'), 10) || 1;
        if (!canCreate) $('#btn-save-bank-op').prop('disabled', true);

        $('.select2').select2({ theme: 'bootstrap-5', width: '100%' });
        $('#op-date').val(new Date().toISOString().slice(0, 10));

        $.getJSON('/api/company/current')
            .done(function () {
                $('#bank-op-company-warning').addClass('d-none');
                loadLookups().always(initDataTable);
            })
            .fail(function () {
                $('#bank-op-company-warning').removeClass('d-none').text('Select a company from the top navbar.');
            });

        $('#op-bank-account-id').on('change', function () {
            var coaId = parseInt($(this).val(), 10) || 0;
            updateBalanceHint($(this), $('#op-bank-balance'), bankAccounts);
            loadNextChequeNumber(coaId);
        });
        $('#op-from-account-id').on('change', function () {
            var fromId = parseInt($(this).val(), 10) || 0;
            buildOptions($('#op-to-account-id'), transferAccounts, fromId);
            updateBalanceHint($(this), $('#op-from-balance'), transferAccounts);
        });
        $('#op-to-account-id').on('change', function () {
            updateBalanceHint($(this), $('#op-to-balance'), transferAccounts);
        });

        $('#bank-op-form').on('submit', saveOperation);

        $(document).on('change', '.deposit-cheque-checkbox', updateDepositSelectedTotal);
        $('#deposit-select-all').on('change', function () {
            var checked = $(this).is(':checked');
            $('.deposit-cheque-checkbox').prop('checked', checked);
            updateDepositSelectedTotal();
        });

        if (transactionType === 2 && canCreate) {
            $('#op-cheque-number-hint').append(
                ' <button type="button" class="btn btn-link btn-sm p-0 align-baseline" id="btn-save-starting-cheque">Save starting #</button>'
            );
            $('#btn-save-starting-cheque').on('click', saveStartingChequeNumber);
        }
    });
})();
