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

    function buildAccountOptions(selectedId) {
        var html = '<option value="">— Select account —</option>';
        accounts.forEach(function (account) {
            var selected = String(account.id) === String(selectedId) ? ' selected' : '';
            html += '<option value="' + account.id + '"' + selected + '>' +
                account.accountNumber + ' — ' + account.accountName + '</option>';
        });
        return html;
    }

    function initAccountSelect($select) {
        if ($.fn.select2) {
            $select.select2({ theme: 'bootstrap-5', width: '100%', dropdownParent: $('#journal-entry-form') });
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
            '<td><select class="form-select form-select-sm line-account" required>' + buildAccountOptions(prefill && prefill.accountId) + '</select></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-debit" min="0" step="0.01" value="' + ((prefill && prefill.debit) || 0) + '" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-credit" min="0" step="0.01" value="' + ((prefill && prefill.credit) || 0) + '" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-memo" maxlength="200" value="' + ((prefill && prefill.memo) || '') + '" /></td>' +
            '<td class="text-end"><button type="button" class="btn btn-link btn-sm text-danger p-0 btn-remove-line" title="Remove"><i class="fa-solid fa-xmark"></i></button></td>' +
            '</tr>'
        );

        $('#je-lines-body').append($row);
        initAccountSelect($row.find('.line-account'));
        recalcTotals();
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
                    debit: line.debit,
                    credit: line.credit,
                    memo: line.memo
                });
            });
            return;
        }

        addLine();
        addLine();
    }

    function loadLookups() {
        if (editEntryId) {
            return $.getJSON('/api/journal-entries/accounts').then(function (accountsData) {
                accounts = accountsData || [];
                if (accounts.length === 0) {
                    showError('No chart of accounts found. Set up accounts under Chart of Accounts first.');
                }
                populateLines();
            });
        }

        return $.when(
            $.getJSON('/api/journal-entries/next-entry-number'),
            $.getJSON('/api/journal-entries/accounts')
        ).then(function (numberRes, accountsRes) {
            $('#entry-number').val(numberRes[0].entryNumber);
            accounts = accountsRes[0] || [];
            if (accounts.length === 0) {
                showError('No chart of accounts found. Set up accounts under Chart of Accounts first.');
            }
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

        $('#je-lines-body tr').each(function () {
            var $row = $(this);
            var accountId = parseInt($row.find('.line-account').val(), 10);
            var debit = parseFloat($row.find('.line-debit').val()) || 0;
            var credit = parseFloat($row.find('.line-credit').val()) || 0;

            if (!accountId) {
                lineValid = false;
                return false;
            }

            lines.push({
                chartOfAccountId: accountId,
                debit: debit,
                credit: credit,
                memo: $row.find('.line-memo').val().trim() || null
            });
        });

        if (!lineValid) {
            showError('Each line must have an account selected.');
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
                loadLookups().fail(function () {
                    showError('Failed to load journal entry data.');
                });
            })
            .fail(function () {
                $('#je-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar before creating a journal entry.');
            });

        $('#je-lines-body').on('input', '.line-debit, .line-credit', recalcTotals);
        $('#je-lines-body').on('click', '.btn-remove-line', function () {
            if ($('#je-lines-body tr').length <= 2) {
                alert('At least two lines are required.');
                return;
            }
            var $select = $(this).closest('tr').find('.line-account');
            if ($select.data('select2')) {
                $select.select2('destroy');
            }
            $(this).closest('tr').remove();
            recalcTotals();
        });

        $('#btn-add-line').on('click', function () {
            addLine();
        });

        $('#journal-entry-form').on('submit', saveEntry);
    });
})();

