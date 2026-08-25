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
    var selectedPartyCache = null;

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

    var amountWordsTimer = null;

    function updateAmountInWords() {
        var $words = $('#op-amount-words');
        if (!$words.length) return;

        var amount = parseFloat($('#op-amount').val());
        if (!amount || amount <= 0) {
            $words.text('');
            return;
        }

        clearTimeout(amountWordsTimer);
        amountWordsTimer = setTimeout(function () {
            $.getJSON('/api/lookup/amount-in-words', { amount: amount })
                .done(function (res) {
                    $words.text(res && res.text ? res.text : '');
                })
                .fail(function () {
                    $words.text('');
                });
        }, 250);
    }

    function readMoney(value) {
        if (value == null || value === '') return null;
        var parsed = parseFloat(value);
        return Number.isNaN(parsed) ? null : parsed;
    }

    function normalizeAccount(a) {
        if (!a) return null;
        var id = parseInt(a.id != null ? a.id : a.Id, 10) || 0;
        if (!id) return null;
        var balance = readMoney(a.balance != null ? a.balance : a.Balance);
        return {
            id: id,
            accountNumber: a.accountNumber || a.AccountNumber || '',
            accountName: a.accountName || a.AccountName || '',
            label: a.label || a.Label,
            balance: balance == null ? 0 : balance
        };
    }

    function normalizeAccountList(list) {
        return (list || []).map(normalizeAccount).filter(Boolean);
    }

    function findAccount(list, id) {
        var target = parseInt(id, 10) || 0;
        if (!target) return null;
        return (list || []).find(function (a) {
            return parseInt(a.id != null ? a.id : a.Id, 10) === target;
        }) || null;
    }

    function buildOptions($select, list, excludeId) {
        var exclude = parseInt(excludeId, 10) || 0;
        $select.find('option:not(:first)').remove();
        (list || []).forEach(function (a) {
            var id = parseInt(a.id != null ? a.id : a.Id, 10) || 0;
            if (!id || (exclude && id === exclude)) return;
            var balance = readMoney(a.balance != null ? a.balance : a.Balance);
            $select.append(
                $('<option></option>')
                    .val(String(id))
                    .text(a.label || a.Label || ((a.accountNumber || a.AccountNumber) + ' — ' + (a.accountName || a.AccountName)))
                    .attr('data-balance', balance == null ? 0 : balance)
            );
        });
        if ($select.data('select2') && window.initPaSelect2) {
            window.initPaSelect2($select, {});
        }
    }

    function partyOptionValue(party) {
        return (party.customerId || 0) + ':' + (party.vendorId || 0) + ':' + party.chartOfAccountId;
    }

    function partySearchText(party, displayText) {
        return [
            party.partyType,
            party.partyName,
            party.accountNumber,
            party.partyCode,
            party.label,
            displayText
        ].filter(Boolean).join(' ').toLowerCase();
    }

    function partySelectMatcher(params, data) {
        if ($.trim(params.term) === '') {
            return data;
        }

        if (data.children && data.children.length) {
            var matchedChildren = [];
            for (var i = 0; i < data.children.length; i++) {
                var childMatch = partySelectMatcher(params, data.children[i]);
                if (childMatch) {
                    matchedChildren.push(childMatch);
                }
            }

            if (matchedChildren.length) {
                return $.extend({}, data, { children: matchedChildren });
            }

            return null;
        }

        var haystack = (data.element && $(data.element).data('search'))
            ? String($(data.element).data('search'))
            : (data.text || '').toLowerCase();

        return haystack.indexOf(params.term.toLowerCase()) > -1 ? data : null;
    }

    function refreshSelect2($select) {
        if (!$select.length) {
            return;
        }

        if (window.initPaSelect2) {
            window.initPaSelect2($select, { matcher: partySelectMatcher });
            return;
        }

        if ($select.data('select2')) {
            $select.select2('destroy');
        }

        $select.select2({
            theme: 'bootstrap-5',
            width: '100%',
            minimumResultsForSearch: 0,
            matcher: partySelectMatcher
        });
    }

    function formatPaymentMethod(value) {
        if (!value) return '—';
        var labels = {
            Cash: 'Cash',
            Cheque: 'Cheque',
            BankTransfer: 'Bank Transfer',
            CashWithdrawal: 'Cash Withdrawal'
        };
        return labels[value] || value;
    }

    function buildPartyOptions($select, list) {
        $select.find('optgroup, option:not(:first)').remove();
        var arParties = (list || []).filter(function (p) { return p.partyType === 'AR'; });
        var apParties = (list || []).filter(function (p) { return p.partyType === 'AP'; });
        var cashParties = (list || []).filter(function (p) { return p.partyType === 'CASH'; });
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
                        .attr('data-search', partySearchText(p, text))
                        .attr('data-customer-id', p.customerId || '')
                        .attr('data-vendor-id', p.vendorId || '')
                        .attr('data-coa-id', p.chartOfAccountId)
                        .attr('data-party-name', p.partyName || '')
                        .attr('data-balance', p.balance != null ? p.balance : 0)
                );
            });
            $select.append($group);
        }

        var taxParties = coaParties.filter(function (p) {
            var name = (p.partyName || '').toLowerCase();
            return name.indexOf('sales tax') >= 0
                || name.indexOf('further tax') >= 0
                || (p.accountNumber || '').indexOf('255') === 0;
        });
        appendGroup('Accounts Receivable (Customers)', arParties);
        appendGroup('Accounts Payable (Vendors)', taxParties.concat(apParties));
        appendGroup('Cash in Hand', cashParties);
        appendGroup('Other Chart of Accounts', coaParties.filter(function (p) {
            var name = (p.partyName || '').toLowerCase();
            return name.indexOf('sales tax') < 0
                && name.indexOf('further tax') < 0
                && (p.accountNumber || '').indexOf('255') !== 0;
        }));
    }

    function pickPartyField(source, camel, pascal) {
        if (!source) return null;
        if (source[camel] != null && source[camel] !== '') return source[camel];
        if (source[pascal] != null && source[pascal] !== '') return source[pascal];
        return null;
    }

    function parsePartyValue(value) {
        var parts = String(value || '').split(':');
        if (parts.length < 3) return null;
        return {
            customerId: parseInt(parts[0], 10) || null,
            vendorId: parseInt(parts[1], 10) || null,
            chartOfAccountId: parseInt(parts[2], 10) || null
        };
    }

    function partyFromSource(source, fallbackValue, fallbackText) {
        var parsed = parsePartyValue(fallbackValue || pickPartyField(source, 'id', 'Id'));
        var customerId = parseInt(pickPartyField(source, 'customerId', 'CustomerId'), 10)
            || (parsed && parsed.customerId)
            || null;
        var vendorId = parseInt(pickPartyField(source, 'vendorId', 'VendorId'), 10)
            || (parsed && parsed.vendorId)
            || null;
        var chartOfAccountId = parseInt(pickPartyField(source, 'chartOfAccountId', 'ChartOfAccountId'), 10)
            || parseInt(pickPartyField(source, 'coaId', 'CoaId'), 10)
            || (parsed && parsed.chartOfAccountId)
            || null;
        var balance = readMoney(pickPartyField(source, 'balance', 'Balance'));
        if (balance == null && source && source.element) {
            balance = readMoney($(source.element).data('balance'));
        }
        if (balance == null) {
            var text = fallbackText || pickPartyField(source, 'text', 'Text') || '';
            var match = String(text).match(/PKR\s+([\d,.-]+)/i);
            if (match) {
                balance = readMoney(match[1].replace(/,/g, ''));
            }
        }

        if (!customerId && !vendorId && !chartOfAccountId) return null;

        return {
            customerId: customerId,
            vendorId: vendorId,
            chartOfAccountId: chartOfAccountId,
            partyName: pickPartyField(source, 'partyName', 'PartyName') || fallbackText || '',
            balance: balance == null ? 0 : balance
        };
    }

    function copyPartyToOption($select, party) {
        var $opt = $select.find('option:selected');
        if (!$opt.length || !$opt.val() || !party) return;
        $opt.attr('data-customer-id', party.customerId || '')
            .attr('data-vendor-id', party.vendorId || '')
            .attr('data-coa-id', party.chartOfAccountId || '')
            .attr('data-party-name', party.partyName || '')
            .attr('data-balance', party.balance != null ? party.balance : 0);
    }

    function updatePartyBalanceHint(party) {
        var selected = party || getSelectedParty();
        $('#op-party-balance').text(
            selected ? 'Outstanding balance: PKR ' + formatMoney(selected.balance) : ''
        );
    }

    function cacheSelectedParty(item, $select) {
        selectedPartyCache = partyFromSource(item, item && (item.id || item.Id), item && (item.text || item.Text));
        if ($select && $select.length) {
            copyPartyToOption($select, selectedPartyCache);
        }
        updatePartyBalanceHint(selectedPartyCache);
    }

    function getSelectedParty() {
        var $select = $('#op-counter-account-id');
        var value = $select.val();
        if (!value) {
            selectedPartyCache = null;
            return null;
        }

        if (selectedPartyCache && partyOptionValue(selectedPartyCache) === String(value)) {
            return selectedPartyCache;
        }

        var selected = $select.data('select2') ? ($select.select2('data')[0] || null) : null;
        var fromSelect2 = partyFromSource(selected, value, selected && selected.text);
        if (fromSelect2) {
            selectedPartyCache = fromSelect2;
            copyPartyToOption($select, fromSelect2);
            return fromSelect2;
        }

        var $opt = $select.find('option:selected');
        if (!$opt.length || !$opt.val()) return null;

        var fromOption = partyFromSource({
            customerId: $opt.data('customerId'),
            vendorId: $opt.data('vendorId'),
            chartOfAccountId: $opt.data('coaId'),
            partyName: $opt.data('partyName'),
            balance: $opt.data('balance'),
            id: $opt.val()
        }, $opt.val(), $opt.text());

        selectedPartyCache = fromOption;
        return fromOption;
    }

    function getPaymentMethod() {
        return parseInt($('#op-payment-method').val(), 10) || 0;
    }

    function updateBalanceHint($select, $hint, list) {
        var id = parseInt($select.val(), 10) || 0;
        if (!id) {
            $hint.text('');
            return;
        }

        var account = findAccount(list, id);
        var balance = account
            ? account.balance
            : readMoney($select.find('option:selected').data('balance'));
        if (account || balance != null) {
            $hint.text('GL balance: PKR ' + formatMoney(balance == null ? 0 : balance));
            return;
        }

        $hint.text('');
    }

    function applyNextChequeNumber(res) {
        if (res && res.nextChequeNumber) {
            $('#op-cheque-number').val(res.nextChequeNumber);
            updateChequeNumberHint({ isConfigured: true });
            return;
        }

        var bankId = parseInt($('#op-bank-account-id').val(), 10) || 0;
        if (bankId) {
            loadNextChequeNumber(bankId);
        }
    }

    function usesChequeNumber(method) {
        return method === 2 || method === 4;
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
        var method = getPaymentMethod();
        if (transactionType !== 2 || (method !== 2 && method !== 4) || !coaId) {
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
        var method = getPaymentMethod();
        if (!canCreate || transactionType !== 2 || (method !== 2 && method !== 4)) return;

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
                var next = res && (res.nextChequeNumber || (res.NextChequeNumber && res.NextChequeNumber.nextChequeNumber));
                if (typeof next === 'string') {
                    $('#op-cheque-number').val(next);
                }
                updateChequeNumberHint({ isConfigured: true });
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save starting cheque number.'));
            });
    }

    function refreshPayFromOptions() {
        if (transactionType !== 2) return;

        var method = getPaymentMethod();
        var $select = $('#op-bank-account-id');
        var list = method === 1 ? transferAccounts : bankAccounts;
        var currentVal = parseInt($select.val(), 10) || 0;

        buildOptions($select, list);
        var restoreId = 0;
        if (currentVal && findAccount(list, currentVal)) {
            restoreId = currentVal;
        } else if (method === 1 && cashAccount) {
            restoreId = cashAccount.id;
        }

        if (restoreId) {
            if (window.setPaSelect2Value) {
                window.setPaSelect2Value($select, String(restoreId));
            } else {
                $select.val(String(restoreId)).trigger('change');
            }
        }

        updateBalanceHint($select, $('#op-bank-balance'), list);
    }

    function syncPaymentMethodUi() {
        if (transactionType !== 2) return;

        var method = getPaymentMethod();
        var $bankWrap = $('#op-pay-from-bank-wrap');
        var $chequeFields = $('#op-cheque-fields');
        var $payToWrap = $('#op-pay-to-wrap');
        var $cashWithdrawalWrap = $('#op-cash-withdrawal-wrap');
        var $bankSelect = $('#op-bank-account-id');
        var $bankLabel = $('label[for="op-bank-account-id"]');
        var $counterSelect = $('#op-counter-account-id');

        $payToWrap.removeClass('d-none');
        $cashWithdrawalWrap.addClass('d-none');
        $counterSelect.prop('required', true);

        if (method === 1) {
            $bankWrap.removeClass('d-none');
            $chequeFields.addClass('d-none');
            $bankSelect.prop('required', true);
            $('#op-cheque-number').prop('required', false).val('');
            $bankLabel.text('Pay From — Bank or Cash in Hand (COA)');
            refreshPayFromOptions();
        } else if (method === 2) {
            $bankWrap.removeClass('d-none');
            $chequeFields.removeClass('d-none');
            $bankSelect.prop('required', true);
            $('#op-cheque-number').prop('required', true);
            $bankLabel.text('Pay From — Bank Account (COA)');
            refreshPayFromOptions();
            var bankId = parseInt($bankSelect.val(), 10) || 0;
            if (bankId) loadNextChequeNumber(bankId);
        } else if (method === 4) {
            $bankWrap.removeClass('d-none');
            $chequeFields.removeClass('d-none');
            $payToWrap.addClass('d-none');
            $cashWithdrawalWrap.removeClass('d-none');
            $bankSelect.prop('required', true);
            $counterSelect.prop('required', false);
            $('#op-cheque-number').prop('required', true);
            $bankLabel.text('Pay From — Bank Account (COA)');
            refreshPayFromOptions();
            var withdrawalBankId = parseInt($bankSelect.val(), 10) || 0;
            if (withdrawalBankId) loadNextChequeNumber(withdrawalBankId);
        } else if (method === 3) {
            $bankWrap.removeClass('d-none');
            $chequeFields.addClass('d-none');
            $bankSelect.prop('required', true);
            $('#op-cheque-number').prop('required', false).val('');
            $bankLabel.text('Pay From — Bank Account (COA)');
            refreshPayFromOptions();
        } else {
            $bankWrap.removeClass('d-none');
            $chequeFields.addClass('d-none');
            $bankSelect.prop('required', false);
            $bankLabel.text('Pay From — Bank Account (COA)');
            refreshPayFromOptions();
        }
    }

    function resolveCashAccount() {
        cashAccount = transferAccounts.find(function (a) {
            return a.accountNumber === '10015';
        }) || null;

        if (cashAccount) {
            var label = cashAccount.label || (cashAccount.accountNumber + ' — ' + cashAccount.accountName);
            $('#op-cash-withdrawal-label').text(label);
            $('#op-cash-withdrawal-balance').text('GL balance: PKR ' + formatMoney(cashAccount.balance));
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
                transferAccounts = normalizeAccountList(res);
                buildOptions($('#op-from-account-id'), transferAccounts);
                buildOptions($('#op-to-account-id'), transferAccounts);
            }));
        } else if (transactionType === 2) {
            requests.push($.getJSON('/api/bank-transactions/coa-banks').done(function (res) {
                bankAccounts = normalizeAccountList(res);
            }));
            requests.push($.getJSON('/api/bank-transactions/coa-transfer').done(function (res) {
                transferAccounts = normalizeAccountList(res);
                resolveCashAccount();
                buildOptions($('#op-bank-account-id'), bankAccounts);
            }));
            if (window.initPaAjaxSelect2) {
                window.initPaAjaxSelect2($('#op-counter-account-id'), {
                    entity: 'party',
                    placeholder: 'Type to search customer, vendor, or account',
                    allowClear: true,
                    limit: 50,
                    onSelect: function (item, $el) {
                        cacheSelectedParty(item, $el);
                    }
                });
            } else {
                requests.push($.getJSON('/api/bank-transactions/coa-counter').done(function (res) {
                    counterAccounts = res || [];
                    buildPartyOptions($('#op-counter-account-id'), counterAccounts);
                    refreshSelect2($('#op-counter-account-id'));
                }));
            }
        } else {
            requests.push($.getJSON('/api/bank-transactions/coa-deposit').done(function (res) {
                bankAccounts = normalizeAccountList(res);
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
            columns.push({
                data: 'paymentMethod',
                defaultContent: '—',
                render: function (d) { return escapeHtml(formatPaymentMethod(d)); }
            });
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

        var selectedParty = null;
        var paymentMethod = transactionType === 2 ? getPaymentMethod() : null;

        if (transactionType === 2) {
            if (paymentMethod === 4) {
                if (!cashAccount) {
                    showFormError('Cash in Hand account is not configured.');
                    return;
                }
                selectedParty = {
                    customerId: null,
                    vendorId: null,
                    chartOfAccountId: cashAccount.id,
                    partyName: cashAccount.accountName || 'Cash in Hand',
                    balance: cashAccount.balance || 0
                };
            } else {
                selectedParty = getSelectedParty();
                if (!selectedParty) {
                    showFormError('Select a pay-to account.');
                    return;
                }
            }
        }

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

            payload.paymentMethod = paymentMethod;
            payload.chartOfAccountId = parseInt($('#op-bank-account-id').val(), 10) || 0;
            if (!payload.chartOfAccountId) {
                showFormError(
                    paymentMethod === 1
                        ? 'Select a bank account or Cash in Hand.'
                        : 'Select a bank account.'
                );
                return;
            }

            if (paymentMethod === 2 || paymentMethod === 4) {
                payload.chequeNumber = $('#op-cheque-number').val()?.trim() || null;
                payload.chequeDate = $('#op-cheque-date').val() || null;
                if (!payload.chequeNumber) {
                    showFormError('Cheque number is required for this payment method.');
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
            .done(function (result) {
                var selectedBankId = parseInt($('#op-bank-account-id').val(), 10) || 0;
                var selectedMethod = getPaymentMethod();
                $('#bank-op-form')[0].reset();
                $('#op-date').val(new Date().toISOString().slice(0, 10));
                $('#op-party-balance').text('');
                selectedPartyCache = null;
                $('#op-amount-words').text('');
                loadLookups().always(function () {
                    if (transactionType === 2) {
                        if (selectedMethod) $('#op-payment-method').val(String(selectedMethod));
                        if (selectedBankId) {
                            $('#op-bank-account-id').val(String(selectedBankId));
                        }
                        syncPaymentMethodUi();
                        if (selectedBankId) {
                            var payFromList = selectedMethod === 1 ? transferAccounts : bankAccounts;
                            updateBalanceHint($('#op-bank-account-id'), $('#op-bank-balance'), payFromList);
                        }
                        if (usesChequeNumber(selectedMethod)) {
                            applyNextChequeNumber(result);
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

        if (window.initPaSelect2) {
            window.initPaSelect2($('.select2').not('#op-counter-account-id'), {});
        } else {
            $('.select2').not('#op-counter-account-id').select2({ theme: 'bootstrap-5', width: '100%', minimumResultsForSearch: 0 });
        }
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

        $('#op-bank-account-id').on('change select2:select', function () {
            var method = getPaymentMethod();
            var list = method === 1 ? transferAccounts : bankAccounts;
            updateBalanceHint($(this), $('#op-bank-balance'), list);
            loadNextChequeNumber(parseInt($(this).val(), 10) || 0);
        });
        $('#op-from-account-id').on('change select2:select', function () {
            var fromId = parseInt($(this).val(), 10) || 0;
            buildOptions($('#op-to-account-id'), transferAccounts, fromId);
            updateBalanceHint($(this), $('#op-from-balance'), transferAccounts);
        });
        $('#op-to-account-id').on('change select2:select', function () {
            updateBalanceHint($(this), $('#op-to-balance'), transferAccounts);
        });
        $('#op-counter-account-id').on('change select2:select', function () {
            updatePartyBalanceHint();
        });
        $('#op-counter-account-id').on('select2:clear', function () {
            selectedPartyCache = null;
            $('#op-party-balance').text('');
        });

        $('#bank-op-form').on('submit', saveOperation);

        $('#op-amount').on('input change', updateAmountInWords);

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
