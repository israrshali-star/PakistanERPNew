(function () {
    'use strict';

    var dataTable = null;
    var canCreate = false;
    var transactionType = 1;
    var bankAccounts = [];
    var transferAccounts = [];
    var counterAccounts = [];
    var cashAccount = null;
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

    function parseDateOnly(value) {
        if (!value) return null;
        var parts = String(value).slice(0, 10).split('-');
        if (parts.length !== 3) return null;
        var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
        return Number.isNaN(d.getTime()) ? null : d;
    }

    function getDepositDate() {
        return parseDateOnly($('#op-date').val());
    }

    function isChequeDepositable(cheque, depositDate) {
        if (!depositDate) return false;
        var chequeDate = parseDateOnly(cheque.chequeDate);
        if (!chequeDate) return true;
        return chequeDate.getTime() <= depositDate.getTime();
    }

    function isChequePostDated(cheque, depositDate) {
        if (!depositDate) return !!cheque.isPostDated;
        var chequeDate = parseDateOnly(cheque.chequeDate);
        if (!chequeDate) return false;
        return chequeDate.getTime() > depositDate.getTime();
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

    function partyOptionValue(party) {
        return (party.customerId || 0) + ':' + (party.vendorId || 0) + ':' + party.chartOfAccountId;
    }

    function buildPartyOptions($select, list) {
        $select.find('optgroup, option:not(:first)').remove();
        var arParties = (list || []).filter(function (p) { return p.partyType === 'AR'; });
        var apParties = (list || []).filter(function (p) { return p.partyType === 'AP'; });
        var coaParties = (list || []).filter(function (p) { return p.partyType === 'COA'; });

        function appendGroup(label, parties) {
            if (!parties.length) return;
            var $group = $('<optgroup></optgroup>').attr('label', label);
            parties.forEach(function (p) {
                var text = (p.label || ('[' + p.partyType + '] ' + p.partyName + ' — ' + p.accountNumber))
                    + ' (PKR ' + formatMoney(p.balance) + ')';
                $group.append(
                    $('<option></option>')
                        .val(partyOptionValue(p))
                        .text(text)
                        .attr('data-customer-id', p.customerId || '')
                        .attr('data-vendor-id', p.vendorId || '')
                        .attr('data-coa-id', p.chartOfAccountId)
                        .attr('data-party-name', p.partyName || '')
                        .attr('data-balance', p.balance || 0)
                );
            });
            $select.append($group);
        }

        appendGroup('Accounts Receivable (Customers)', arParties);
        appendGroup('Accounts Payable (Vendors)', apParties);
        appendGroup('Other Chart of Accounts', coaParties);
    }

    function getSelectedParty() {
        var $opt = $('#op-counter-account-id option:selected');
        if (!$opt.length || !$opt.val()) return null;

        var parts = String($opt.val()).split(':');
        return {
            customerId: parseInt($opt.data('customerId') || parts[0], 10) || null,
            vendorId: parseInt($opt.data('vendorId') || parts[1], 10) || null,
            chartOfAccountId: parseInt($opt.data('coaId') || parts[2], 10) || null,
            partyName: $opt.data('partyName') || $opt.text(),
            balance: parseFloat($opt.data('balance')) || 0
        };
    }

    function getPaymentMethod() {
        return parseInt($('#op-payment-method').val(), 10) || 0;
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
        if (transactionType !== 2 || getPaymentMethod() !== 2 || !coaId) {
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
        if (!canCreate || transactionType !== 2 || getPaymentMethod() !== 2) return;

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

    function syncPaymentMethodUi() {
        if (transactionType !== 2) return;

        var method = getPaymentMethod();
        var $bankWrap = $('#op-pay-from-bank-wrap');
        var $cashWrap = $('#op-pay-from-cash-wrap');
        var $chequeFields = $('#op-cheque-fields');
        var $bankSelect = $('#op-bank-account-id');

        if (method === 1) {
            $bankWrap.addClass('d-none');
            $cashWrap.removeClass('d-none');
            $chequeFields.addClass('d-none');
            $bankSelect.prop('required', false);
            $('#op-cheque-number').prop('required', false);
        } else if (method === 2) {
            $bankWrap.removeClass('d-none');
            $cashWrap.addClass('d-none');
            $chequeFields.removeClass('d-none');
            $bankSelect.prop('required', true);
            $('#op-cheque-number').prop('required', true);
            var bankId = parseInt($bankSelect.val(), 10) || 0;
            if (bankId) loadNextChequeNumber(bankId);
        } else if (method === 3) {
            $bankWrap.removeClass('d-none');
            $cashWrap.addClass('d-none');
            $chequeFields.addClass('d-none');
            $bankSelect.prop('required', true);
            $('#op-cheque-number').prop('required', false).val('');
        } else {
            $bankWrap.removeClass('d-none');
            $cashWrap.addClass('d-none');
            $chequeFields.addClass('d-none');
            $bankSelect.prop('required', false);
        }
    }

    function resolveCashAccount() {
        cashAccount = transferAccounts.find(function (a) {
            return a.accountNumber === '10015';
        }) || null;

        if (cashAccount) {
            $('#op-cash-account-id').val(String(cashAccount.id));
            $('#op-cash-account-label').text(cashAccount.label || (cashAccount.accountNumber + ' — ' + cashAccount.accountName));
            $('#op-cash-balance').text('GL balance: PKR ' + formatMoney(cashAccount.balance));
        }
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

        var depositDate = getDepositDate();
        $body.empty();
        if (!undepositedCheques.length) {
            $body.append(
                '<tr><td colspan="7" class="text-center text-muted py-3">No undeposited cheques.</td></tr>'
            );
            updateDepositSelectedTotal();
            return;
        }

        undepositedCheques.forEach(function (cheque) {
            var chequeNumber = cheque.chequeNumber ? escapeHtml(cheque.chequeNumber) : '—';
            var chequeDateText = cheque.chequeDate ? formatDate(cheque.chequeDate) : '—';
            var postDated = isChequePostDated(cheque, depositDate);
            var depositable = isChequeDepositable(cheque, depositDate);
            var badge = postDated
                ? ' <span class="badge bg-warning text-dark ms-1">Post-dated</span>'
                : '';
            var disabledAttr = depositable ? '' : ' disabled title="Cannot deposit before cheque date"';
            var rowClass = depositable ? '' : ' class="table-secondary text-muted"';

            $body.append(
                '<tr' + rowClass + '>' +
                '<td><input type="checkbox" class="form-check-input deposit-cheque-checkbox" value="' + cheque.id + '" data-amount="' + cheque.amount + '" data-cheque-date="' + (cheque.chequeDate || '') + '"' + disabledAttr + ' /></td>' +
                '<td>' + escapeHtml(cheque.customerName) + '</td>' +
                '<td>' + escapeHtml(cheque.receiptNumber) + '</td>' +
                '<td>' + chequeNumber + '</td>' +
                '<td>' + chequeDateText + badge + '</td>' +
                '<td>' + formatDate(cheque.receiptDate) + '</td>' +
                '<td class="text-end text-currency">' + formatMoney(cheque.amount) + '</td>' +
                '</tr>'
            );
        });
        updateDepositSelectedTotal();
    }

    function getEnabledDepositCheckboxes() {
        return $('.deposit-cheque-checkbox:not(:disabled)');
    }

    function updateDepositSelectedTotal() {
        var total = 0;
        $('.deposit-cheque-checkbox:checked:not(:disabled)').each(function () {
            total += parseFloat($(this).data('amount')) || 0;
        });
        $('#deposit-selected-total').text(formatMoney(total));

        var $enabled = getEnabledDepositCheckboxes();
        $('#deposit-select-all').prop(
            'checked',
            $enabled.length > 0 && $enabled.filter(':checked').length === $enabled.length
        );
    }

    function getSelectedDepositReceiptIds() {
        return $('.deposit-cheque-checkbox:checked:not(:disabled)').map(function () {
            return parseInt($(this).val(), 10);
        }).get().filter(function (id) { return id > 0; });
    }

    function validateDepositSelection() {
        var depositDate = getDepositDate();
        if (!depositDate) {
            return 'Deposit date is required.';
        }

        var blocked = [];
        $('.deposit-cheque-checkbox:checked:not(:disabled)').each(function () {
            var chequeDate = parseDateOnly($(this).data('chequeDate'));
            if (chequeDate && chequeDate.getTime() > depositDate.getTime()) {
                blocked.push($(this).closest('tr').find('td').eq(3).text().trim());
            }
        });

        if (blocked.length) {
            return 'Cannot deposit post-dated cheques before their cheque date: ' + blocked.join(', ');
        }

        return null;
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
        } else if (transactionType === 2) {
            requests.push($.getJSON('/api/bank-transactions/coa-banks').done(function (res) {
                bankAccounts = res || [];
                buildOptions($('#op-bank-account-id'), bankAccounts);
            }));
            requests.push($.getJSON('/api/bank-transactions/coa-transfer').done(function (res) {
                transferAccounts = res || [];
                resolveCashAccount();
            }));
            requests.push($.getJSON('/api/bank-transactions/coa-counter').done(function (res) {
                counterAccounts = res || [];
                buildPartyOptions($('#op-counter-account-id'), counterAccounts);
            }));
        } else {
            requests.push($.getJSON('/api/bank-transactions/coa-deposit').done(function (res) {
                bankAccounts = res || [];
                buildOptions($('#op-bank-account-id'), bankAccounts);
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
            columns.push({ data: 'paymentMethod', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } });
            columns.push({ data: 'chequeNumber', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } });
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

        var selectedParty = transactionType === 2 ? getSelectedParty() : null;
        var paymentMethod = transactionType === 2 ? getPaymentMethod() : null;
        var payload = {
            transactionType: transactionType,
            transactionDate: $('#op-date').val(),
            amount: parseFloat($('#op-amount').val()) || 0,
            description: $('#op-description').val().trim() || null,
            partyName: selectedParty ? selectedParty.partyName : null,
            counterChartOfAccountId: selectedParty ? selectedParty.chartOfAccountId : null,
            customerId: selectedParty ? selectedParty.customerId : null,
            vendorId: selectedParty ? selectedParty.vendorId : null
        };

        if (transactionType === 2) {
            if (!paymentMethod) {
                showFormError('Select a payment method.');
                return;
            }
            if (!selectedParty) {
                showFormError('Select a pay-to account.');
                return;
            }

            payload.paymentMethod = paymentMethod;

            if (paymentMethod === 1) {
                payload.chartOfAccountId = parseInt($('#op-cash-account-id').val(), 10) || 0;
                if (!payload.chartOfAccountId) {
                    showFormError('Cash in Hand account is not configured.');
                    return;
                }
            } else {
                payload.chartOfAccountId = parseInt($('#op-bank-account-id').val(), 10) || 0;
                if (!payload.chartOfAccountId) {
                    showFormError('Select a bank account.');
                    return;
                }
            }

            if (paymentMethod === 2) {
                payload.chequeNumber = $('#op-cheque-number').val()?.trim() || null;
                payload.chequeDate = $('#op-cheque-date').val() || null;
                if (!payload.chequeNumber) {
                    showFormError('Cheque number is required for cheque payments.');
                    return;
                }
            }
        } else if (transactionType === 1) {
            payload.customerReceiptIds = getSelectedDepositReceiptIds();
            payload.chartOfAccountId = parseInt($('#op-bank-account-id').val(), 10) || 0;
            if (!payload.customerReceiptIds.length) {
                showFormError('Select at least one cheque to deposit.');
                return;
            }
            var depositValidationError = validateDepositSelection();
            if (depositValidationError) {
                showFormError(depositValidationError);
                return;
            }
        } else if (transactionType === 3) {
            payload.chartOfAccountId = parseInt($('#op-from-account-id').val(), 10) || 0;
            payload.transferToChartOfAccountId = parseInt($('#op-to-account-id').val(), 10) || null;
        }

        $.ajax({
            url: '/api/bank-transactions',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                var selectedBankId = parseInt($('#op-bank-account-id').val(), 10) || 0;
                var selectedMethod = getPaymentMethod();
                $('#bank-op-form')[0].reset();
                $('#op-date').val(new Date().toISOString().slice(0, 10));
                $('#op-party-balance').text('');
                loadLookups().always(function () {
                    if (transactionType === 2) {
                        if (selectedMethod) $('#op-payment-method').val(String(selectedMethod));
                        syncPaymentMethodUi();
                        if (selectedBankId && selectedMethod !== 1) {
                            $('#op-bank-account-id').val(String(selectedBankId));
                            updateBalanceHint($('#op-bank-account-id'), $('#op-bank-balance'), bankAccounts);
                            if (selectedMethod === 2) loadNextChequeNumber(selectedBankId);
                        }
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
                loadLookups().always(function () {
                    syncPaymentMethodUi();
                    initDataTable();
                });
            })
            .fail(function () {
                $('#bank-op-company-warning').removeClass('d-none').text('Select a company from the top navbar.');
            });

        $('#op-payment-method').on('change', syncPaymentMethodUi);

        $('#op-bank-account-id').on('change', function () {
            updateBalanceHint($(this), $('#op-bank-balance'), bankAccounts);
            loadNextChequeNumber(parseInt($(this).val(), 10) || 0);
        });
        $('#op-from-account-id').on('change', function () {
            var fromId = parseInt($(this).val(), 10) || 0;
            buildOptions($('#op-to-account-id'), transferAccounts, fromId);
            updateBalanceHint($(this), $('#op-from-balance'), transferAccounts);
        });
        $('#op-to-account-id').on('change', function () {
            updateBalanceHint($(this), $('#op-to-balance'), transferAccounts);
        });
        $('#op-counter-account-id').on('change', function () {
            var party = getSelectedParty();
            $('#op-party-balance').text(
                party ? 'Outstanding balance: PKR ' + formatMoney(party.balance) : ''
            );
        });

        $('#bank-op-form').on('submit', saveOperation);

        $(document).on('change', '.deposit-cheque-checkbox', updateDepositSelectedTotal);
        $('#deposit-select-all').on('change', function () {
            var checked = $(this).is(':checked');
            getEnabledDepositCheckboxes().prop('checked', checked);
            updateDepositSelectedTotal();
        });

        if (transactionType === 1) {
            $('#op-date').on('change', renderUndepositedCheques);
        }

        if (transactionType === 2 && canCreate) {
            $('#op-cheque-number-hint').append(
                ' <button type="button" class="btn btn-link btn-sm p-0 align-baseline" id="btn-save-starting-cheque">Save starting #</button>'
            );
            $('#btn-save-starting-cheque').on('click', saveStartingChequeNumber);
        }
    });
})();
