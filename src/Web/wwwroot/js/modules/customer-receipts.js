(function () {
    'use strict';

    var receiptModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;
    var customers = [];

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
        var d = new Date(value);
        if (isNaN(d.getTime())) {
            return value;
        }
        return d.toLocaleDateString('en-GB');
    }

    function toInputDate(value) {
        if (!value) {
            return '';
        }
        var d = new Date(value);
        if (isNaN(d.getTime())) {
            return '';
        }
        return d.toISOString().slice(0, 10);
    }

    function showFormError(message) {
        $('#receipt-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#receipt-form-error').addClass('d-none').text('');
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

    function ensureCompanySelected() {
        return $.getJSON('/api/company/current');
    }

    function togglePaymentFields() {
        var method = parseInt($('#payment-method').val(), 10) || 1;
        var showBank = method === 2 || method === 3;
        var showCheque = method === 2;

        $('.bank-fields').toggleClass('d-none', !showBank);
        $('.cheque-fields').toggleClass('d-none', !showCheque);

        if (!showCheque) {
            $('#cheque-number, #cheque-date').val('');
        }
        if (!showBank) {
            $('#receipt-bank-id').val('').trigger('change');
        }
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
        $('#receipt-customer-id').val('').trigger('change');
        $('#payment-method').val('1');
        $('#receipt-bank-id').val('').trigger('change');
        $('#cheque-number, #cheque-date, #receipt-notes').val('');
        togglePaymentFields();
        updateCustomerBalanceHint();
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/customer-receipts/customers'),
            $.getJSON('/api/customer-receipts/banks')
        ).then(function (customersRes, banksRes) {
            customers = customersRes[0] || [];

            var $customer = $('#receipt-customer-id');
            $customer.find('option:not(:first)').remove();
            customers.forEach(function (c) {
                $customer.append(
                    $('<option></option>')
                        .val(c.id)
                        .text(c.buyerId + ' — ' + c.buyerName)
                );
            });

            var $bank = $('#receipt-bank-id');
            $bank.find('option:not(:first)').remove();
            (banksRes[0] || []).forEach(function (b) {
                $bank.append(
                    $('<option></option>')
                        .val(b.id)
                        .text(b.bankName + ' (' + b.accountNumber + ')')
                );
            });
        });
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
                error: function (xhr) {
                    if (xhr.status === 400) {
                        showCompanyWarning(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[2, 'desc']],
            columns: [
                { data: 'receiptNumber' },
                { data: 'customerName' },
                {
                    data: 'receiptDate',
                    render: function (data) { return formatDate(data); }
                },
                {
                    data: 'amount',
                    className: 'text-end',
                    render: function (data) { return formatMoney(data); }
                },
                { data: 'paymentMethod' },
                {
                    data: 'bankName',
                    render: function (data) { return data ? escapeHtml(data) : '—'; }
                },
                {
                    data: null,
                    orderable: false,
                    className: 'text-end',
                    render: function (data, type, row) {
                        var buttons = [];
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
        resetReceiptForm();
        $('#receiptModalLabel').text('New Customer Receipt');
        clearFormError();

        $.getJSON('/api/customer-receipts/next-receipt-number')
            .done(function (res) {
                $('#receipt-number').val(res.receiptNumber);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not generate receipt number.'));
            });

        receiptModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $('#receiptModalLabel').text('Edit Customer Receipt');

        $.getJSON('/api/customer-receipts/' + id)
            .done(function (receipt) {
                $('#receipt-id').val(receipt.id);
                $('#receipt-number').val(receipt.receiptNumber);
                $('#receipt-date').val(toInputDate(receipt.receiptDate));
                $('#receipt-amount').val(receipt.amount);
                $('#receipt-customer-id').val(receipt.customerId).trigger('change');
                $('#payment-method').val(receipt.paymentMethod).trigger('change');
                $('#receipt-bank-id').val(receipt.bankId || '').trigger('change');
                $('#cheque-number').val(receipt.chequeNumber || '');
                $('#cheque-date').val(toInputDate(receipt.chequeDate));
                $('#receipt-notes').val(receipt.notes || '');
                togglePaymentFields();
                updateCustomerBalanceHint();
                receiptModal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load receipt.'));
            });
    }

    function buildPayload() {
        var bankId = parseInt($('#receipt-bank-id').val(), 10) || 0;
        var chequeDate = $('#cheque-date').val();

        return {
            id: parseInt($('#receipt-id').val(), 10) || null,
            receiptNumber: $('#receipt-number').val().trim(),
            customerId: parseInt($('#receipt-customer-id').val(), 10) || 0,
            receiptDate: $('#receipt-date').val(),
            amount: parseFloat($('#receipt-amount').val()) || 0,
            paymentMethod: parseInt($('#payment-method').val(), 10) || 1,
            bankId: bankId > 0 ? bankId : null,
            chequeNumber: $('#cheque-number').val().trim() || null,
            chequeDate: chequeDate || null,
            notes: $('#receipt-notes').val().trim() || null
        };
    }

    function saveReceipt(e) {
        e.preventDefault();
        clearFormError();

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
            .done(function () {
                receiptModal.hide();
                dataTable.ajax.reload(null, false);
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

        $('#receipt-customer-id, #receipt-bank-id').select2({
            theme: 'bootstrap-5',
            width: '100%',
            dropdownParent: $('#receiptModal')
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
        $('#receipt-customer-id').on('change', updateCustomerBalanceHint);
        $('#receipt-form').on('submit', saveReceipt);

        $('#customer-receipts-table').on('click', '.btn-edit-receipt', function () {
            openEditModal($(this).data('id'));
        });

        $('#customer-receipts-table').on('click', '.btn-delete-receipt', function () {
            deleteReceipt($(this).data('id'));
        });
    });
})();
