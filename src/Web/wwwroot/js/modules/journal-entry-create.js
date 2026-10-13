(function () {
    'use strict';

    var accounts = [];
    var lineCounter = 0;
    var editEntryId = null;

    function showError(message) {
        $('#je-form-error').removeClass('d-none').text(message);
    }

    function clearError() {
        $('#je-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function formatAmount(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function accountField(account, camel, pascal) {
        if (!account) {
            return '';
        }
        var value = account[camel];
        return value == null || value === '' ? account[pascal] : value;
    }

    function accountNumberOf(account) {
        return String(accountField(account, 'accountNumber', 'AccountNumber') || '');
    }

    function accountOptionLabel(account) {
        var number = accountNumberOf(account);
        var name = accountField(account, 'accountName', 'AccountName');
        if (number === '11110') {
            name = 'Accounts Receivable';
        } else if (number === '20000') {
            name = 'Accounts Payable';
        }
        return [name, number].filter(Boolean).join(' — ');
    }

    function isReceivableOrPayable(account) {
        var number = accountNumberOf(account);
        return number === '11110' || number === '20000';
    }

    function accountOptionHtml(account, selected) {
        var id = String(accountField(account, 'id', 'Id') || '');
        if (!id) {
            return '';
        }
        var isSelected = id === selected;
        return '<option value="' + id + '"' + (isSelected ? ' selected' : '') + '>' +
            escapeHtml(accountOptionLabel(account)) + '</option>';
    }

    function orderedAccounts() {
        var controlAccounts = [];
        var otherAccounts = [];
        (accounts || []).forEach(function (account) {
            if (isReceivableOrPayable(account)) {
                controlAccounts.push(account);
            } else {
                otherAccounts.push(account);
            }
        });
        controlAccounts.sort(function (a, b) {
            return accountNumberOf(a).localeCompare(accountNumberOf(b));
        });
        return controlAccounts.concat(otherAccounts);
    }

    function buildAccountOptions(selectedId, selectedText) {
        var html = '<option value="">— Select account —</option>';
        var selected = selectedId ? String(selectedId) : '';
        var found = false;

        orderedAccounts().forEach(function (account) {
            if (String(accountField(account, 'id', 'Id')) === selected) {
                found = true;
            }
            html += accountOptionHtml(account, selected);
        });

        if (selected && !found) {
            html += '<option value="' + selected + '" selected>' +
                escapeHtml(selectedText || selected) + '</option>';
        }
        return html;
    }

    function initAccountSelect($select) {
        if (!$.fn.select2) {
            return;
        }

        try {
            $select.select2({
                theme: 'bootstrap-5',
                width: '100%',
                dropdownParent: $('#journal-entry-form'),
                placeholder: 'Select account',
                allowClear: true,
                minimumResultsForSearch: 0
            });
        } catch (e) {
            // Keep the native list usable if the enhanced dropdown cannot start.
        }
    }

    function recalcTotals() {
        var totalDebit = 0;
        var totalCredit = 0;

        $('#je-lines-body tr').each(function () {
            totalDebit += parseFloat($(this).find('.line-debit').val()) || 0;
            totalCredit += parseFloat($(this).find('.line-credit').val()) || 0;
        });

        $('#total-debit').text(formatAmount(totalDebit));
        $('#total-credit').text(formatAmount(totalCredit));

        var $badge = $('#balance-status');
        if (Math.abs(totalDebit - totalCredit) < 0.01 && totalDebit > 0) {
            $badge.removeClass('bg-danger bg-secondary').addClass('bg-success').text('Balanced');
        } else if (totalDebit === 0 && totalCredit === 0) {
            $badge.removeClass('bg-success bg-danger').addClass('bg-secondary').text('Enter amounts');
        } else {
            $badge.removeClass('bg-success bg-secondary').addClass('bg-danger').text('Out of balance');
        }
    }

    function addLine(prefill) {
        lineCounter += 1;
        var $row = $(
            '<tr data-line-id="line-' + lineCounter + '">' +
            '<td><select class="form-select form-select-sm line-account" required>' +
            buildAccountOptions(prefill && prefill.accountId, prefill && prefill.accountText) +
            '</select></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-debit" min="0" step="0.01" value="' + ((prefill && prefill.debit) || 0) + '" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-credit" min="0" step="0.01" value="' + ((prefill && prefill.credit) || 0) + '" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-memo" maxlength="200" value="' + escapeHtml((prefill && prefill.memo) || '') + '" /></td>' +
            '<td><select class="form-select form-select-sm line-party d-none"><option value=""></option></select></td>' +
            '<td class="text-end"><button type="button" class="btn btn-link btn-sm text-danger p-0 btn-remove-line" title="Remove"><i class="fa-solid fa-xmark"></i></button></td>' +
            '</tr>'
        );

        $('#je-lines-body').append($row);
        $row.data('syncing-account', true);
        initAccountSelect($row.find('.line-account'));
        $row.data('syncing-account', false);
        syncPartySelect($row, prefill);
        recalcTotals();
    }

    function partyModeForAccountId(accountId) {
        var account = (accounts || []).filter(function (item) {
            return String(accountField(item, 'id', 'Id')) === String(accountId || '');
        })[0];
        var number = account ? accountNumberOf(account) : '';
        if (number === '11110' || number === '11000') {
            return 'customer';
        }
        if (number === '20000') {
            return 'vendor';
        }
        return '';
    }

    function syncPartySelect($row, prefill) {
        var mode = partyModeForAccountId($row.find('.line-account').val());
        var $party = $row.find('.line-party');
        var previousMode = $party.data('party-mode') || '';
        if ($party.data('select2')) {
            $party.select2('destroy');
        }
        $party.empty().append('<option value=""></option>');
        $party.data('party-mode', mode);
        $party.toggleClass('d-none', !mode);
        if (!mode || !window.initPaAjaxSelect2) {
            return;
        }

        var placeholder = mode === 'customer' ? 'Select customer' : 'Select vendor';
        window.initPaAjaxSelect2($party, {
            entity: mode,
            dropdownParent: '#journal-entry-form',
            placeholder: placeholder,
            allowClear: true,
            limit: 100
        });

        var partyId = prefill && (prefill.customerId || prefill.vendorId || prefill.partyId);
        var partyText = prefill && (prefill.partyName || prefill.partyText);
        var prefillMode = prefill && prefill.customerId ? 'customer' : (prefill && prefill.vendorId ? 'vendor' : previousMode);
        if (partyId && prefillMode === mode && window.setPaSelect2Value) {
            window.setPaSelect2Value($party, partyId, partyText || partyId, { trigger: false });
        }
    }

    function loadExistingLines() {
        var $body = $('#je-lines-body');
        var raw = $body.attr('data-lines');
        if (!raw) {
            return [];
        }

        try {
            return JSON.parse(raw) || [];
        } catch (e) {
            return [];
        }
    }

    function populateLines() {
        if ($('#je-lines-body tr').length > 0) {
            return;
        }

        var existingLines = loadExistingLines();
        if (existingLines.length > 0) {
            existingLines.forEach(function (line) {
                addLine({
                    accountId: line.accountId,
                    accountText: line.accountText ||
                        ([line.accountNumber, line.accountName].filter(Boolean).join(' — ')),
                    debit: line.debit,
                    credit: line.credit,
                    memo: line.memo,
                    customerId: line.customerId,
                    vendorId: line.vendorId,
                    partyName: line.partyName
                });
            });
            return;
        }

        addLine();
        addLine();
    }

    function loadLookups() {
        var accountsRequest = $.getJSON('/api/journal-entries/accounts').done(function (list) {
            accounts = $.isArray(list) ? list : [];
        });
        var numberRequest = editEntryId
            ? $.Deferred().resolve().promise()
            : $.getJSON('/api/journal-entries/next-entry-number').done(function (numberRes) {
                var number = numberRes && (numberRes.entryNumber || numberRes.EntryNumber);
                if (number) {
                    $('#entry-number').val(number);
                }
            });

        return $.when(accountsRequest, numberRequest).done(function () {
            populateLines();
        });
    }

    function saveEntry(e) {
        e.preventDefault();
        clearError();

        var entryDate = $('#entry-date').val();
        if (!entryDate) {
            showError('Please enter an entry date.');
            return;
        }

        var lines = [];
        var lineValid = true;
        var lineError = '';

        $('#je-lines-body tr').each(function () {
            var $row = $(this);
            var accountId = parseInt($row.find('.line-account').val(), 10);
            var debit = parseFloat($row.find('.line-debit').val()) || 0;
            var credit = parseFloat($row.find('.line-credit').val()) || 0;

            if (!accountId) {
                lineValid = false;
                lineError = 'Each line must have an account selected.';
                return false;
            }

            var partyMode = $row.find('.line-party').data('party-mode') || '';
            var partyId = parseInt($row.find('.line-party').val(), 10) || null;
            if (partyMode === 'customer' && !partyId) {
                lineError = 'Select a customer for Accounts Receivable.';
                lineValid = false;
                return false;
            }
            if (partyMode === 'vendor' && !partyId) {
                lineError = 'Select a vendor for Accounts Payable.';
                lineValid = false;
                return false;
            }

            lines.push({
                chartOfAccountId: accountId,
                debit: debit,
                credit: credit,
                memo: $row.find('.line-memo').val().trim() || null,
                customerId: partyMode === 'customer' ? partyId : null,
                vendorId: partyMode === 'vendor' ? partyId : null
            });
        });

        if (!lineValid) {
            showError(lineError || 'Each line must have an account selected.');
            return;
        }

        if (lines.length < 2) {
            showError('Add at least two journal lines.');
            return;
        }

        var payload = {
            entryNumber: $('#entry-number').val().trim(),
            entryDate: entryDate,
            description: $('#entry-description').val().trim() || null,
            lines: lines
        };

        var $btn = $('#journal-entry-form button[type="submit"]');
        $btn.prop('disabled', true);

        var url = editEntryId ? '/api/journal-entries/' + editEntryId : '/api/journal-entries';
        var method = editEntryId ? 'PUT' : 'POST';

        $.ajax({
            url: url,
            method: method,
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function (result) {
                var entryId = editEntryId || (result && (result.entryId || result.EntryId));
                window.location.href = entryId
                    ? '/JournalEntries/Details/' + entryId
                    : '/JournalEntries';
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Failed to save journal entry.'));
            })
            .always(function () {
                $btn.prop('disabled', false);
            });
    }

    $(function () {
        var $edit = $('#journal-entry-edit');
        if ($edit.length) {
            editEntryId = $edit.data('entry-id');
        }

        if (!editEntryId) {
            var today = new Date();
            $('#entry-date').val(
                today.getFullYear() + '-' +
                String(today.getMonth() + 1).padStart(2, '0') + '-' +
                String(today.getDate()).padStart(2, '0')
            );
        }

        $.getJSON('/api/company/current')
            .done(function () {
                loadLookups().fail(function (xhr) {
                    if (!$('#je-lines-body tr').length) {
                        populateLines();
                    }
                    showError(getApiErrorMessage(xhr, 'Failed to load journal entry data.'));
                });
            })
            .fail(function () {
                $('#je-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar before creating a journal entry.');
            });

        $('#je-lines-body').on('input', '.line-debit, .line-credit', recalcTotals);
        $('#je-lines-body').on('change', '.line-account', function () {
            var $row = $(this).closest('tr');
            if ($row.data('syncing-account')) {
                return;
            }
            syncPartySelect($row);
        });
        $('#je-lines-body').on('click', '.btn-remove-line', function () {
            if ($('#je-lines-body tr').length <= 2) {
                alert('At least two lines are required.');
                return;
            }
            var $select = $(this).closest('tr').find('.line-account, .line-party');
            $select.each(function () {
                if ($(this).data('select2')) {
                    $(this).select2('destroy');
                }
            });
            $(this).closest('tr').remove();
            recalcTotals();
        });

        $('#btn-add-line').on('click', function () {
            addLine();
        });

        $('#journal-entry-form').on('submit', saveEntry);
    });
})();

