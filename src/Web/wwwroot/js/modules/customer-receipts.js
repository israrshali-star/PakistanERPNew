(function () {
    'use strict';

    var receiptModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;
    var customers = [];
    var returnAfterEditUrl = null;
    var openedFromReturnUrl = false;

    function isSafeReturnUrl(url) {
        if (!url || typeof url !== 'string') {
            return false;
        }
        if (url.charAt(0) !== '/' || url.indexOf('//') === 0) {
            return false;
        }
        return url.indexOf('/Customers/Ledger/') === 0
            || url.indexOf('/Customers/Statement/') === 0;
    }

    function goBackToReturnUrl() {
        if (!returnAfterEditUrl || !isSafeReturnUrl(returnAfterEditUrl)) {
            return false;
        }
        var url = returnAfterEditUrl;
        returnAfterEditUrl = null;
        openedFromReturnUrl = false;
        window.location.href = url;
        return true;
    }

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatMoney(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatDate(value) {
        if (!value) {
            return '';
        }
        if (typeof value === 'string') {
            var match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value.trim());
            if (match) {
                return match[3] + '/' + match[2] + '/' + match[1];
            }
        }
        var d = value instanceof Date ? value : new Date(value);
        if (isNaN(d.getTime())) {
            return value;
        }
        return d.toLocaleDateString('en-GB');
    }

    function toInputDate(value) {
        if (!value) {
            return '';
        }

        // Prefer the calendar date from the API string (yyyy-MM-dd…) so timezone
        // conversion does not move the posting date to the previous day.
        if (typeof value === 'string') {
            var match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value.trim());
            if (match) {
                return match[1] + '-' + match[2] + '-' + match[3];
            }
        }

        var d = value instanceof Date ? value : new Date(value);
        if (isNaN(d.getTime())) {
            return '';
        }

        var year = d.getFullYear();
        var month = String(d.getMonth() + 1).padStart(2, '0');
        var day = String(d.getDate()).padStart(2, '0');
        return year + '-' + month + '-' + day;
    }

    function showFormError(message) {
        $('#receipt-form-success').addClass('d-none').text('');
        $('#receipt-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#receipt-form-error').addClass('d-none').text('');
    }

    function showFormSuccess(message) {
        $('#receipt-form-error').addClass('d-none').text('');
        $('#receipt-form-success').removeClass('d-none').text(message);
    }

    function clearFormSuccess() {
        $('#receipt-form-success').addClass('d-none').text('');
    }

    function clearFormMessages() {
        clearFormError();
        clearFormSuccess();
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || body.title || body.detail || fallback;
    }

    function showCompanyWarning(message) {
        $('#receipt-company-warning')
            .removeClass('d-none')
            .text(message || 'Select a company from the top navbar to manage customer receipts.');
    }

    function hideCompanyWarning() {
        $('#receipt-company-warning').addClass('d-none').text('');
    }

    var amountWordsTimer = null;

    function updateAmountInWords() {
        var $words = $('#receipt-amount-words');
        var amount = parseFloat($('#receipt-amount').val());
        if (!$words.length) {
            return;
        }
        if (!amount || amount <= 0) {
            $words.text('');
            return;
        }

        clearTimeout(amountWordsTimer);
        amountWordsTimer = setTimeout(function () {
            $.getJSON('/api/lookup/amount-in-words', { amount: amount })
                .done(function (res) {
                    $words.text(res.text || '');
                })
                .fail(function () {
                    $words.text('');
                });
        }, 250);
    }

    function ensureCompanySelected() {
        return $.getJSON('/api/company/current');
    }

    var DRAWN_ON_PREFIX = 'Drawn on: ';

    function parseNotesFields(notes) {
        if (!notes) {
            return { drawnOnBank: '', userNotes: '' };
        }
        var lines = notes.split('\n');
        if (lines[0].indexOf(DRAWN_ON_PREFIX) === 0) {
            return {
                drawnOnBank: lines[0].substring(DRAWN_ON_PREFIX.length).trim(),
                userNotes: lines.slice(1).join('\n').trim()
            };
        }
        return { drawnOnBank: '', userNotes: notes.trim() };
    }

    function buildNotesPayload(drawnOnBank, userNotes) {
        var parts = [];
        if (drawnOnBank && drawnOnBank.trim()) {
            parts.push(DRAWN_ON_PREFIX + drawnOnBank.trim());
        }
        if (userNotes && userNotes.trim()) {
            parts.push(userNotes.trim());
        }
        return parts.length ? parts.join('\n') : null;
    }

    function getChequeBankType() {
        if ($('#cheque-type-same-bank').is(':checked')) {
            return 1;
        }
        if ($('#cheque-type-other-bank').is(':checked')) {
            return 2;
        }
        return null;
    }

    function setChequeBankType(value) {
        $('#cheque-type-same-bank').prop('checked', value === 1);
        $('#cheque-type-other-bank').prop('checked', value === 2);
        toggleChequeTypeFields();
    }

    function toggleChequeTypeFields() {
        var chequeType = getChequeBankType();
        var isSameBank = chequeType === 1;
        var isOtherBank = chequeType === 2;

        $('.same-bank-cheque-fields').toggleClass('d-none', !isSameBank);
        $('.other-bank-cheque-fields').toggleClass('d-none', !isOtherBank);

        $('#same-bank-id, #same-bank-cheque-number, #same-bank-cheque-date').prop('required', isSameBank);
        $('#cheque-number, #cheque-date').prop('required', isOtherBank);

        if (!isSameBank) {
            $('#same-bank-id').val('').trigger('change');
            $('#same-bank-cheque-number, #same-bank-cheque-date').val('');
        }
        if (!isOtherBank) {
            $('#cheque-number, #cheque-date, #cheque-drawn-bank').val('');
        }
    }

    function togglePaymentFields() {
        var method = parseInt($('#payment-method').val(), 10) || 1;
        var isCheque = method === 2;
        var isBankTransfer = method === 3;

        $('.cheque-panel').toggleClass('d-none', !isCheque);
        $('.bank-transfer-fields').toggleClass('d-none', !isBankTransfer);

        $('#receipt-bank-id').prop('required', isBankTransfer);

        if (!isCheque) {
            setChequeBankType(null);
        } else if (!getChequeBankType()) {
            setChequeBankType(2);
        } else {
            toggleChequeTypeFields();
        }

        if (!isBankTransfer) {
            $('#receipt-bank-id').val('').trigger('change');
        }

        var $notesLabel = $('label[for="receipt-notes"]');
        var $notesHint = $('#receipt-notes-hint');
        if (isBankTransfer) {
            $notesLabel.text('Ref No / Notes');
            if ($notesHint.length) {
                $notesHint.removeClass('d-none').text('Shown as Check/Ref No on the payment receipt.');
            }
        } else {
            $notesLabel.text('Notes');
            if ($notesHint.length) {
                $notesHint.addClass('d-none').text('');
            }
        }
    }

    function setFormReadOnly(isReadOnly) {
        $('#receipt-form')
            .find('input, select, textarea, button[type="submit"]')
            .not('[data-bs-dismiss="modal"]')
            .not('#receipt-attachment-upload')
            .prop('disabled', isReadOnly);
        $('#btn-generate-receipt-number').prop('disabled', isReadOnly);
        $('#receipt-attachment-upload').prop('disabled', false);
        if (isReadOnly) {
            $('#receipt-deposited-warning').removeClass('d-none');
        } else {
            $('#receipt-deposited-warning').addClass('d-none');
        }
    }

    function clearAttachmentUi() {
        $('#receipt-attachment-upload').val('');
        $('#receipt-attachments-list').empty();
    }

    function renderAttachments(attachments) {
        var $list = $('#receipt-attachments-list');
        $list.empty();
        var items = attachments || [];
        if (!items.length) {
            return;
        }

        items.forEach(function (item) {
            var id = item.id || item.Id;
            var name = item.fileName || item.FileName || 'file';
            var size = item.fileSizeBytes || item.FileSizeBytes || 0;
            var sizeKb = size ? (size / 1024).toFixed(1) + ' KB' : '';
            var $row = $('<li class="list-group-item d-flex justify-content-between align-items-center px-0"></li>');
            $row.append(
                $('<a></a>')
                    .attr('href', '/api/customer-receipts/attachments/' + id + '/download')
                    .attr('target', '_blank')
                    .text(name + (sizeKb ? ' (' + sizeKb + ')' : ''))
            );
            if (canEdit) {
                $row.append(
                    $('<button type="button" class="btn btn-sm btn-outline-danger btn-delete-receipt-attachment"></button>')
                        .attr('data-id', id)
                        .attr('title', 'Delete attachment')
                        .html('<i class="fa-solid fa-trash"></i>')
                );
            }
            $list.append($row);
        });
    }

    function uploadReceiptFiles(receiptId, files) {
        if (!receiptId || !files || !files.length) {
            return $.Deferred().resolve().promise();
        }

        var uploads = Array.prototype.map.call(files, function (file) {
            var formData = new FormData();
            formData.append('file', file);
            return $.ajax({
                url: '/api/customer-receipts/' + receiptId + '/attachments',
                method: 'POST',
                data: formData,
                processData: false,
                contentType: false
            });
        });

        return $.when.apply($, uploads);
    }

    function reloadAttachments(receiptId) {
        if (!receiptId) {
            clearAttachmentUi();
            return;
        }

        $.getJSON('/api/customer-receipts/' + receiptId + '/attachments')
            .done(function (items) {
                renderAttachments(items);
            })
            .fail(function () {
                // keep existing list on failure
            });
    }

    function updateCustomerBalanceHint() {
        var customerId = parseInt($('#receipt-customer-id').val(), 10) || 0;
        var customer = customers.find(function (c) { return c.id === customerId; });
        if (customer) {
            $('#receipt-customer-balance')
                .text('Outstanding balance: PKR ' + formatMoney(customer.balance));
        } else {
            $('#receipt-customer-balance').text('');
        }
    }

    function resetReceiptForm() {
        $('#receipt-id').val('');
        $('#receipt-number').val('');
        $('#receipt-date').val(toInputDate(new Date()));
        $('#receipt-amount').val('');
        $('#receipt-amount-words').text('');
        $('#receipt-customer-id').val('').trigger('change');
        $('#payment-method').val('1');
        $('#receipt-bank-id, #same-bank-id').val('').trigger('change');
        setChequeBankType(null);
        $('#same-bank-cheque-number, #same-bank-cheque-date, #cheque-number, #cheque-date, #cheque-drawn-bank, #receipt-notes').val('');
        clearAttachmentUi();
        setFormReadOnly(false);
        togglePaymentFields();
        updateCustomerBalanceHint();
    }

    function prepareFormForNextReceipt() {
        resetReceiptForm();
        clearFormMessages();
        $('#receiptModalLabel').text('New Customer Receipt');

        $.getJSON('/api/customer-receipts/next-receipt-number')
            .done(function (res) {
                $('#receipt-number').val(res.receiptNumber);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not generate receipt number.'));
            });
    }

    function initSelect2($element) {
        if ($element.data('select2')) {
            $element.select2('destroy');
        }

        $element.select2({
            theme: 'bootstrap-5',
            width: '100%',
            dropdownParent: $('#receiptModal'),
            minimumResultsForSearch: 0
        });
    }

    function populateCustomerSelect(customersList) {
        var $customer = $('#receipt-customer-id');
        var selectedCustomer = $customer.val();

        if ($customer.data('select2')) {
            $customer.select2('destroy');
        }

        $customer.find('option:not(:first)').remove();
        (customersList || []).forEach(function (c) {
            $customer.append(
                $('<option></option>')
                    .val(c.id)
                    .text(c.buyerId + ' — ' + c.buyerName)
            );
        });

        if (selectedCustomer) {
            $customer.val(selectedCustomer);
        }

        initSelect2($customer);
    }

    function populateBankSelect(banksList) {
        var $bank = $('#receipt-bank-id');
        var $sameBank = $('#same-bank-id');
        var selectedBank = $bank.val();
        var selectedSameBank = $sameBank.val();

        [$bank, $sameBank].forEach(function ($el) {
            if ($el.data('select2')) {
                $el.select2('destroy');
            }

            $el.find('option:not(:first)').remove();
            (banksList || []).forEach(function (b) {
                $el.append(
                    $('<option></option>')
                        .val(b.id)
                        .text(b.bankName + ' (' + b.accountNumber + ')')
                );
            });
        });

        if (selectedBank && $bank.find('option[value="' + selectedBank + '"]').length) {
            $bank.val(selectedBank);
        } else {
            $bank.val('');
        }

        if (selectedSameBank && $sameBank.find('option[value="' + selectedSameBank + '"]').length) {
            $sameBank.val(selectedSameBank);
        } else {
            $sameBank.val('');
        }

        initSelect2($bank);
        initSelect2($sameBank);
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/customer-receipts/customers'),
            $.getJSON('/api/customer-receipts/banks')
        ).then(function (customersRes, banksRes) {
            customers = customersRes[0] || [];
            populateCustomerSelect(customers);
            populateBankSelect(banksRes[0] || []);
        });
    }

    function initDefaultDateFilters() {
        var today = new Date();
        var todayStr = toInputDate(today);
        $('#filter-from').val(todayStr);
        $('#filter-to').val(todayStr);
    }

    function reloadDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
        }
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#customer-receipts-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/customer-receipts/datatable',
                data: function (d) {
                    d.fromDate = $('#filter-from').val();
                    d.toDate = $('#filter-to').val();
                },
                error: function (xhr) {
                    if (xhr.status === 400) {
                        showCompanyWarning(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[2, 'asc']],
            columns: [
                { data: 'receiptNumber' },
                { data: 'customerName' },
                {
                    data: 'receiptDate',
                    render: function (data) { return formatDate(data); }
                },
                {
                    data: 'amount',
                    className: 'text-end text-currency',
                    render: function (data) { return formatMoney(data); }
                },
                { data: 'paymentMethod' },
                {
                    data: 'chequeNumber',
                    render: function (data) { return data ? escapeHtml(data) : '—'; }
                },
                {
                    data: 'chequeDate',
                    render: function (data) { return data ? formatDate(data) : '—'; }
                },
                {
                    data: 'depositStatus',
                    render: function (data) {
                        if (!data || data === '—') return '—';
                        if (data.indexOf('Awaiting Approval') >= 0) {
                            return '<span class="badge bg-primary">' + escapeHtml(data) + '</span>';
                        }
                        if (data.indexOf('In Clearing') >= 0 || data.indexOf('Post-dated') >= 0) {
                            return '<span class="badge bg-warning text-dark">' + escapeHtml(data) + '</span>';
                        }
                        if (data === 'Cleared') {
                            return '<span class="badge bg-success">' + escapeHtml(data) + '</span>';
                        }
                        if (data.indexOf('Returned') >= 0) {
                            return '<span class="badge bg-danger">' + escapeHtml(data) + '</span>';
                        }
                        return escapeHtml(data);
                    }
                },
                {
                    data: null,
                    orderable: false,
                    className: 'text-end',
                    render: function (data, type, row) {
                        var buttons = [
                            '<button type="button" class="btn btn-sm btn-outline-danger btn-print-receipt me-1" data-id="' + row.id + '" title="Print / PDF">' +
                            '<i class="fa-solid fa-print"></i></button>',
                            '<button type="button" class="btn btn-sm btn-outline-success btn-share-receipt" data-id="' + row.id + '" title="Share on WhatsApp">' +
                            '<i class="fa-brands fa-whatsapp"></i></button>'
                        ];
                        if (canEdit && row.depositStatus === 'Deposited (Awaiting Approval)') {
                            buttons.push(
                                '<button type="button" class="btn btn-sm btn-success btn-approve-clearance" data-id="' + row.id + '" title="Approve clearance">' +
                                '<i class="fa-solid fa-check"></i></button>'
                            );
                        }
                        if (canEdit && row.canMarkReturned) {
                            buttons.push(
                                '<button type="button" class="btn btn-sm btn-outline-danger btn-mark-returned ms-1" data-id="' + row.id + '" title="Mark cheque returned / not cleared">' +
                                '<i class="fa-solid fa-rotate-left"></i></button>'
                            );
                        }
                        if (row.depositStatus === 'Returned (Not Cleared)') {
                            return buttons.join('') || '<span class="text-muted small">Returned</span>';
                        }
                        if (row.depositStatus === 'Cleared' || row.depositStatus === 'Deposited (Awaiting Approval)') {
                            if (!buttons.length) {
                                return '<span class="text-muted small">Locked</span>';
                            }
                            return buttons.join('');
                        }
                        if (canEdit) {
                            buttons.push(
                                '<button type="button" class="btn btn-sm btn-outline-primary btn-edit-receipt" data-id="' + row.id + '" title="Edit">' +
                                '<i class="fa-solid fa-pen"></i></button>'
                            );
                        }
                        if (canDelete) {
                            buttons.push(
                                '<button type="button" class="btn btn-sm btn-outline-danger btn-delete-receipt ms-1" data-id="' + row.id + '" title="Delete">' +
                                '<i class="fa-solid fa-trash"></i></button>'
                            );
                        }
                        return buttons.join('') || '—';
                    }
                }
            ]
        });
    }

    function openCreateModal() {
        clearFormMessages();
        $('#receiptModalLabel').text('New Customer Receipt');
        resetReceiptForm();

        loadLookups()
            .done(function () {
                $.getJSON('/api/customer-receipts/next-receipt-number')
                    .done(function (res) {
                        $('#receipt-number').val(res.receiptNumber);
                    })
                    .fail(function (xhr) {
                        showFormError(getApiErrorMessage(xhr, 'Could not generate receipt number.'));
                    });

                receiptModal.show();
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not load customers and bank accounts for the selected company.'));
            });
    }

    function openEditModal(id) {
        clearFormError();
        clearFormMessages();
        $('#receiptModalLabel').text('Edit Customer Receipt');

        $.when(loadLookups(), $.getJSON('/api/customer-receipts/' + id))
            .done(function (_, receiptRes) {
                var receipt = receiptRes[0];
                var noteFields = parseNotesFields(receipt.notes || '');

                $('#receipt-id').val(receipt.id);
                $('#receipt-number').val(receipt.receiptNumber);
                $('#receipt-date').val(toInputDate(receipt.receiptDate));
                $('#receipt-amount').val(receipt.amount);
                $('#receipt-customer-id').val(receipt.customerId).trigger('change');
                $('#payment-method').val(receipt.paymentMethod).trigger('change');
                setChequeBankType(receipt.chequeBankType || 2);
                $('#receipt-bank-id').val(receipt.bankId || '').trigger('change');
                $('#same-bank-id').val(receipt.chequeBankType === 1 ? (receipt.bankId || '') : '').trigger('change');
                if (receipt.chequeBankType === 1) {
                    $('#same-bank-cheque-number').val(receipt.chequeNumber || '');
                    $('#same-bank-cheque-date').val(toInputDate(receipt.chequeDate));
                } else {
                    $('#cheque-number').val(receipt.chequeNumber || '');
                    $('#cheque-date').val(toInputDate(receipt.chequeDate));
                    $('#cheque-drawn-bank').val(noteFields.drawnOnBank);
                }
                $('#receipt-notes').val(noteFields.userNotes);
                togglePaymentFields();
                setFormReadOnly(receipt.isDeposited === true || !!receipt.clearedAt);
                updateCustomerBalanceHint();
                updateAmountInWords();
                renderAttachments(receipt.attachments || receipt.Attachments || []);
                receiptModal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load receipt.'));
            });
    }

    function validateReceiptForm() {
        var method = parseInt($('#payment-method').val(), 10) || 1;

        if (method === 2) {
            var chequeType = getChequeBankType();
            if (!chequeType) {
                showFormError('Select Same Bank or Other Bank for cheque payments.');
                return false;
            }

            if (chequeType === 1) {
                if (!$('#same-bank-id').val()) {
                    showFormError('Select the bank account for same-bank cheques.');
                    return false;
                }
                if (!$('#same-bank-cheque-number').val().trim()) {
                    showFormError('Enter the cheque number.');
                    return false;
                }
                if (!$('#same-bank-cheque-date').val()) {
                    showFormError('Enter the cheque date.');
                    return false;
                }
            }

            if (chequeType === 2) {
                if (!$('#cheque-number').val().trim()) {
                    showFormError('Enter the cheque number in the other bank cheque details.');
                    return false;
                }
                if (!$('#cheque-date').val()) {
                    showFormError('Enter the cheque date in the other bank cheque details.');
                    return false;
                }
            }
        }

        if (method === 3 && !$('#receipt-bank-id').val()) {
            showFormError('Select the bank account for bank transfer.');
            return false;
        }

        return true;
    }

    function buildPayload() {
        var method = parseInt($('#payment-method').val(), 10) || 1;
        var bankId = parseInt($('#receipt-bank-id').val(), 10) || 0;
        var sameBankId = parseInt($('#same-bank-id').val(), 10) || 0;
        var chequeType = getChequeBankType();
        var chequeNumber = null;
        var chequeDate = null;
        var notes = $('#receipt-notes').val().trim() || null;

        if (method === 2 && chequeType === 1) {
            chequeNumber = $('#same-bank-cheque-number').val().trim();
            chequeDate = $('#same-bank-cheque-date').val() || null;
        } else if (method === 2 && chequeType === 2) {
            chequeNumber = $('#cheque-number').val().trim();
            chequeDate = $('#cheque-date').val() || null;
            notes = buildNotesPayload($('#cheque-drawn-bank').val(), $('#receipt-notes').val());
        } else if (method === 3 && notes) {
            // Bank transfer: notes (ref.no) also go into Check/Ref No.
            chequeNumber = notes.length > 50 ? notes.substring(0, 50) : notes;
        }

        return {
            id: parseInt($('#receipt-id').val(), 10) || null,
            receiptNumber: $('#receipt-number').val().trim(),
            customerId: parseInt($('#receipt-customer-id').val(), 10) || 0,
            receiptDate: $('#receipt-date').val(),
            amount: parseFloat($('#receipt-amount').val()) || 0,
            paymentMethod: method,
            chequeBankType: method === 2 ? chequeType : null,
            bankId: method === 3 && bankId > 0
                ? bankId
                : (method === 2 && chequeType === 1 && sameBankId > 0 ? sameBankId : null),
            chequeNumber: (method === 2 || method === 3) ? chequeNumber : null,
            chequeDate: method === 2 && chequeDate ? chequeDate : null,
            notes: notes
        };
    }

    function saveReceipt(e) {
        e.preventDefault();
        clearFormMessages();

        if (!validateReceiptForm()) {
            return;
        }

        var payload = buildPayload();
        var id = payload.id;
        var method = id ? 'PUT' : 'POST';
        var url = id ? '/api/customer-receipts/' + id : '/api/customer-receipts';

        $.ajax({
            url: url,
            method: method,
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function (res) {
                dataTable.ajax.reload(null, false);
                var savedMsg = payload.paymentMethod === 2
                    ? (payload.chequeBankType === 1
                        ? (id ? 'Same-bank cheque updated and cleared.' : 'Same-bank cheque saved and cleared. Enter another receipt or close when finished.')
                        : (id
                            ? 'Other-bank cheque updated. It remains on the undeposited list until deposited via Make Deposit.'
                            : 'Other-bank cheque saved. It will appear on Banking → Make Deposit. Enter another receipt or close when finished.'))
                    : (id ? 'Receipt updated. Close when finished.' : 'Receipt saved. Enter another receipt or close when finished.');

                var savedReceipt = res.receipt || res.Receipt;
                var savedId = (savedReceipt && (savedReceipt.id || savedReceipt.Id)) || id;
                var pendingFiles = $('#receipt-attachment-upload')[0].files;

                var afterAttachments = function () {
                    if (id && openedFromReturnUrl && goBackToReturnUrl()) {
                        return;
                    }

                    loadLookups().always(function () {
                        if (id) {
                            showFormSuccess(savedMsg);
                            updateCustomerBalanceHint();
                            reloadAttachments(savedId);
                            $('#receipt-attachment-upload').val('');
                        } else {
                            prepareFormForNextReceipt();
                            showFormSuccess(savedMsg);
                        }
                    });
                };

                if (savedId && pendingFiles && pendingFiles.length) {
                    uploadReceiptFiles(savedId, pendingFiles)
                        .done(afterAttachments)
                        .fail(function (xhr) {
                            showFormError(getApiErrorMessage(xhr, 'Receipt saved, but document upload failed.'));
                            if (id) {
                                reloadAttachments(savedId);
                            }
                        });
                } else {
                    afterAttachments();
                }
            })
            .fail(function (xhr) {
                var body = xhr.responseJSON;
                var message = body && (body.message || body.Message)
                    ? (body.message || body.Message)
                    : getApiErrorMessage(xhr, 'Could not save receipt.');
                showFormError(message);
            });
    }

    function deleteReceipt(id) {
        if (!confirm('Delete this customer receipt?')) {
            return;
        }

        $.ajax({
            url: '/api/customer-receipts/' + id,
            method: 'DELETE'
        })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not delete receipt.'));
            });
    }

    $(function () {
        var $perms = $('#receipt-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;

        if (!canCreate) {
            $('#btn-add-receipt').remove();
        }

        receiptModal = new bootstrap.Modal(document.getElementById('receiptModal'));

        initDefaultDateFilters();
        $('#btn-apply-filter').on('click', reloadDataTable);
        $('#filter-from, #filter-to').on('change', reloadDataTable);

        if ($.fn.select2) {
            $('#payment-method').select2({
                theme: 'bootstrap-5',
                width: '100%',
                dropdownParent: $('#receiptModal'),
                minimumResultsForSearch: 0
            });
        }

        document.getElementById('receiptModal').addEventListener('hidden.bs.modal', function () {
            if (openedFromReturnUrl && goBackToReturnUrl()) {
                return;
            }
            resetReceiptForm();
            clearFormMessages();
            $('#receiptModalLabel').text('New Customer Receipt');
        });

        ensureCompanySelected()
            .done(function () {
                hideCompanyWarning();
                loadLookups().always(initDataTable);
            })
            .fail(function () {
                showCompanyWarning();
            });

        $('#btn-add-receipt').on('click', openCreateModal);
        $('#btn-generate-receipt-number').on('click', function () {
            $.getJSON('/api/customer-receipts/next-receipt-number')
                .done(function (res) {
                    $('#receipt-number').val(res.receiptNumber);
                })
                .fail(function (xhr) {
                    showFormError(getApiErrorMessage(xhr, 'Could not generate receipt number.'));
                });
        });

        $('#payment-method').on('change', togglePaymentFields);
        $('#receipt-amount').on('input change', updateAmountInWords);
        $('.cheque-type-option').on('change', function () {
            var $target = $(this);
            if ($target.is(':checked')) {
                $('.cheque-type-option').not($target).prop('checked', false);
            }
            toggleChequeTypeFields();
        });
        $('#receipt-customer-id').on('change', updateCustomerBalanceHint);
        $('#receipt-form').on('submit', saveReceipt);

        $('#customer-receipts-table').on('click', '.btn-print-receipt', function () {
            var id = $(this).data('id');
            var pdfUrl = '/api/customer-receipts/' + id + '/pdf';
            if (window.PrintChoice) {
                window.PrintChoice.open({
                    title: 'Print customer receipt',
                    summary: 'Choose Print for the printer dialog, or Save as PDF to download the receipt PDF.',
                    pdfUrl: pdfUrl
                });
                return;
            }
            if (window.ReceiptShare && window.ReceiptShare.print) {
                window.ReceiptShare.print(id);
            } else {
                window.open(pdfUrl, '_blank');
            }
        });

        $('#receipt-attachment-upload').on('change', function () {
            var receiptId = parseInt($('#receipt-id').val(), 10) || 0;
            var input = this;
            if (!receiptId || !input.files || !input.files.length) {
                return;
            }

            uploadReceiptFiles(receiptId, input.files)
                .done(function () {
                    showFormSuccess('Document uploaded.');
                    reloadAttachments(receiptId);
                })
                .fail(function (xhr) {
                    showFormError(getApiErrorMessage(xhr, 'Failed to upload document.'));
                })
                .always(function () {
                    input.value = '';
                });
        });

        $('#receipt-attachments-list').on('click', '.btn-delete-receipt-attachment', function () {
            var attachmentId = $(this).data('id');
            var receiptId = parseInt($('#receipt-id').val(), 10) || 0;
            if (!confirm('Delete this document?')) {
                return;
            }

            $.ajax({
                url: '/api/customer-receipts/attachments/' + attachmentId,
                method: 'DELETE'
            })
                .done(function () {
                    showFormSuccess('Document deleted.');
                    reloadAttachments(receiptId);
                })
                .fail(function (xhr) {
                    showFormError(getApiErrorMessage(xhr, 'Failed to delete document.'));
                });
        });

        $('#customer-receipts-table').on('click', '.btn-share-receipt', function () {
            if (window.ReceiptShare) {
                window.ReceiptShare.open($(this).data('id'));
            }
        });

        $('#customer-receipts-table').on('click', '.btn-edit-receipt', function () {
            openEditModal($(this).data('id'));
        });

        $('#customer-receipts-table').on('click', '.btn-delete-receipt', function () {
            deleteReceipt($(this).data('id'));
        });

        $('#customer-receipts-table').on('click', '.btn-approve-clearance', function () {
            approveClearance($(this).data('id'));
        });

        $('#customer-receipts-table').on('click', '.btn-mark-returned', function () {
            markChequeReturned($(this).data('id'));
        });

        var params = new URLSearchParams(window.location.search);
        var editId = parseInt(params.get('edit') || '', 10);
        var returnUrl = params.get('returnUrl') || '';
        if (returnUrl && isSafeReturnUrl(returnUrl)) {
            returnAfterEditUrl = returnUrl;
        }
        if (editId > 0) {
            if (returnAfterEditUrl) {
                openedFromReturnUrl = true;
            }
            openEditModal(editId);
            if (window.history && window.history.replaceState) {
                var cleanUrl = window.location.pathname + window.location.hash;
                window.history.replaceState({}, document.title, cleanUrl);
            }
        }
    });

    function markChequeReturned(id) {
        var reason = prompt('Reason for return / market not clear (optional):');
        if (reason === null) {
            return;
        }

        if (!confirm('Mark this cheque as returned? A reverse GL entry will be posted and customer balance restored.')) {
            return;
        }

        $.ajax({
            url: '/api/customer-receipts/' + id + '/mark-returned',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ reason: reason.trim() || null })
        })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not mark cheque as returned.'));
            });
    }

    function approveClearance(id) {
        if (!confirm('Bank has cleared this cheque? Customer balance and bank account will be updated.')) {
            return;
        }

        $.ajax({
            url: '/api/customer-receipts/' + id + '/approve-clearance',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({})
        })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not approve cheque clearance.'));
            });
    }
})();
