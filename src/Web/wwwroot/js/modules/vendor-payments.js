(function () {
    'use strict';

    var paymentModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;
    var vendors = [];

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
        $('#payment-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#payment-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || body.title || body.detail || fallback;
    }

    function showCompanyWarning(message) {
        $('#payment-company-warning')
            .removeClass('d-none')
            .text(message || 'Select a company from the top navbar to manage vendor payments.');
    }

    function hideCompanyWarning() {
        $('#payment-company-warning').addClass('d-none').text('');
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
            $('#payment-bank-id').val('').trigger('change');
        }
    }

    function updateVendorBalanceHint() {
        var vendorId = parseInt($('#payment-vendor-id').val(), 10) || 0;
        var vendor = vendors.find(function (v) { return v.id === vendorId; });
        if (vendor) {
            $('#payment-vendor-balance')
                .text('Outstanding payable: PKR ' + formatMoney(vendor.balance));
        } else {
            $('#payment-vendor-balance').text('');
        }
    }

    function resetPaymentForm() {
        $('#payment-id').val('');
        $('#payment-number').val('');
        $('#payment-date').val(toInputDate(new Date()));
        $('#payment-amount').val('');
        $('#payment-vendor-id').val('').trigger('change');
        $('#payment-method').val('1');
        $('#payment-bank-id').val('').trigger('change');
        $('#cheque-number, #cheque-date, #payment-notes').val('');
        togglePaymentFields();
        updateVendorBalanceHint();
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/vendor-payments/vendors'),
            $.getJSON('/api/vendor-payments/banks')
        ).then(function (vendorsRes, banksRes) {
            vendors = vendorsRes[0] || [];

            var $vendor = $('#payment-vendor-id');
            $vendor.find('option:not(:first)').remove();
            vendors.forEach(function (v) {
                $vendor.append(
                    $('<option></option>')
                        .val(v.id)
                        .text(v.vendorCode + ' — ' + v.vendorName)
                );
            });

            var $bank = $('#payment-bank-id');
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

        dataTable = $('#vendor-payments-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/vendor-payments/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        showCompanyWarning(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[2, 'desc']],
            columns: [
                { data: 'paymentNumber' },
                { data: 'vendorName' },
                {
                    data: 'paymentDate',
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
                                '<button type="button" class="btn btn-sm btn-outline-primary btn-edit-payment" data-id="' + row.id + '" title="Edit">' +
                                '<i class="fa-solid fa-pen"></i></button>'
                            );
                        }
                        if (canDelete) {
                            buttons.push(
                                '<button type="button" class="btn btn-sm btn-outline-danger btn-delete-payment ms-1" data-id="' + row.id + '" title="Delete">' +
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
        resetPaymentForm();
        $('#paymentModalLabel').text('New Vendor Payment');
        clearFormError();

        $.getJSON('/api/vendor-payments/next-payment-number')
            .done(function (res) {
                $('#payment-number').val(res.paymentNumber);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not generate payment number.'));
            });

        paymentModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $('#paymentModalLabel').text('Edit Vendor Payment');

        $.getJSON('/api/vendor-payments/' + id)
            .done(function (payment) {
                $('#payment-id').val(payment.id);
                $('#payment-number').val(payment.paymentNumber);
                $('#payment-date').val(toInputDate(payment.paymentDate));
                $('#payment-amount').val(payment.amount);
                $('#payment-vendor-id').val(payment.vendorId).trigger('change');
                $('#payment-method').val(payment.paymentMethod).trigger('change');
                $('#payment-bank-id').val(payment.bankId || '').trigger('change');
                $('#cheque-number').val(payment.chequeNumber || '');
                $('#cheque-date').val(toInputDate(payment.chequeDate));
                $('#payment-notes').val(payment.notes || '');
                togglePaymentFields();
                updateVendorBalanceHint();
                paymentModal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load payment.'));
            });
    }

    function buildPayload() {
        var bankId = parseInt($('#payment-bank-id').val(), 10) || 0;
        var chequeDate = $('#cheque-date').val();

        return {
            id: parseInt($('#payment-id').val(), 10) || null,
            paymentNumber: $('#payment-number').val().trim(),
            vendorId: parseInt($('#payment-vendor-id').val(), 10) || 0,
            paymentDate: $('#payment-date').val(),
            amount: parseFloat($('#payment-amount').val()) || 0,
            paymentMethod: parseInt($('#payment-method').val(), 10) || 1,
            bankId: bankId > 0 ? bankId : null,
            chequeNumber: $('#cheque-number').val().trim() || null,
            chequeDate: chequeDate || null,
            notes: $('#payment-notes').val().trim() || null
        };
    }

    function savePayment(e) {
        e.preventDefault();
        clearFormError();

        var payload = buildPayload();
        var id = payload.id;
        var method = id ? 'PUT' : 'POST';
        var url = id ? '/api/vendor-payments/' + id : '/api/vendor-payments';

        $.ajax({
            url: url,
            method: method,
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                paymentModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                var body = xhr.responseJSON;
                var message = body && (body.message || body.Message)
                    ? (body.message || body.Message)
                    : getApiErrorMessage(xhr, 'Could not save payment.');
                showFormError(message);
            });
    }

    function deletePayment(id) {
        if (!confirm('Delete this vendor payment?')) {
            return;
        }

        $.ajax({
            url: '/api/vendor-payments/' + id,
            method: 'DELETE'
        })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not delete payment.'));
            });
    }

    $(function () {
        var $perms = $('#payment-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;

        if (!canCreate) {
            $('#btn-add-payment').remove();
        }

        paymentModal = new bootstrap.Modal(document.getElementById('paymentModal'));

        $('#payment-vendor-id, #payment-bank-id').select2({
            theme: 'bootstrap-5',
            width: '100%',
            dropdownParent: $('#paymentModal')
        });

        ensureCompanySelected()
            .done(function () {
                hideCompanyWarning();
                loadLookups().always(initDataTable);
            })
            .fail(function () {
                showCompanyWarning();
            });

        $('#btn-add-payment').on('click', openCreateModal);
        $('#btn-generate-payment-number').on('click', function () {
            $.getJSON('/api/vendor-payments/next-payment-number')
                .done(function (res) {
                    $('#payment-number').val(res.paymentNumber);
                })
                .fail(function (xhr) {
                    showFormError(getApiErrorMessage(xhr, 'Could not generate payment number.'));
                });
        });

        $('#payment-method').on('change', togglePaymentFields);
        $('#payment-vendor-id').on('change', updateVendorBalanceHint);
        $('#payment-form').on('submit', savePayment);

        $('#vendor-payments-table').on('click', '.btn-edit-payment', function () {
            openEditModal($(this).data('id'));
        });

        $('#vendor-payments-table').on('click', '.btn-delete-payment', function () {
            deletePayment($(this).data('id'));
        });
    });
})();
