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

    var amountWordsTimer = null;

    function updateAmountInWords() {
        var $words = $('#payment-amount-words');
        var amount = parseFloat($('#payment-amount').val());
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

    function cacheVendor(item) {
        if (!item || item.id == null || item.id === '') {
            return;
        }

        var id = parseInt(item.id, 10);
        if (!id) {
            return;
        }

        var mapped = {
            id: id,
            vendorCode: item.vendorCode,
            vendorName: item.vendorName,
            balance: item.balance
        };
        var index = vendors.findIndex(function (v) { return v.id === id; });
        if (index >= 0) {
            vendors[index] = mapped;
        } else {
            vendors.push(mapped);
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
        $('#payment-amount-words').text('');
        $('#payment-vendor-id').val('').trigger('change');
        $('#payment-method').val('1');
        $('#payment-bank-id').val('').trigger('change');
        $('#cheque-number, #cheque-date, #payment-notes').val('');
        togglePaymentFields();
        updateVendorBalanceHint();
    }

    var lookupsReady = false;

    function loadLookups() {
        if (lookupsReady) {
            return $.Deferred().resolve().promise();
        }

        if (window.initPaAjaxSelect2) {
            window.initPaAjaxSelect2($('#payment-vendor-id'), {
                entity: 'vendor',
                dropdownParent: '#paymentModal',
                placeholder: 'Type to search vendor',
                onSelect: function (item) {
                    cacheVendor(item);
                    updateVendorBalanceHint();
                }
            });
            window.initPaAjaxSelect2($('#payment-bank-id'), {
                entity: 'bank',
                dropdownParent: '#paymentModal',
                placeholder: 'Type to search bank'
            });
        }

        lookupsReady = true;
        return $.Deferred().resolve().promise();
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

        dataTable = $('#vendor-payments-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/vendor-payments/datatable',
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
                        var buttons = [
                            '<button type="button" class="btn btn-sm btn-outline-success btn-share-payment" data-id="' + row.id + '" title="Share on WhatsApp">' +
                            '<i class="fa-brands fa-whatsapp"></i></button>'
                        ];
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
        $('#paymentModalLabel').text('New Vendor Payment');
        clearFormError();

        loadLookups()
            .done(function () {
                resetPaymentForm();

                $.getJSON('/api/vendor-payments/next-payment-number')
                    .done(function (res) {
                        $('#payment-number').val(res.paymentNumber);
                    })
                    .fail(function (xhr) {
                        showFormError(getApiErrorMessage(xhr, 'Could not generate payment number.'));
                    });

                paymentModal.show();
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not load vendors and bank accounts for the selected company.'));
            });
    }

    function openEditModal(id) {
        clearFormError();
        $('#paymentModalLabel').text('Edit Vendor Payment');

        $.when(loadLookups(), $.getJSON('/api/vendor-payments/' + id))
            .done(function (_, paymentRes) {
                var payment = paymentRes[0];
                $('#payment-id').val(payment.id);
                $('#payment-number').val(payment.paymentNumber);
                $('#payment-date').val(toInputDate(payment.paymentDate));
                $('#payment-amount').val(payment.amount);
                var vendorLabel = [payment.vendorCode, payment.vendorName].filter(Boolean).join(' — ');
                if (window.setPaSelect2Value) {
                    window.setPaSelect2Value($('#payment-vendor-id'), payment.vendorId, vendorLabel, { trigger: false });
                } else {
                    $('#payment-vendor-id').val(payment.vendorId);
                }
                $.getJSON('/api/lookup/search', { entity: 'vendor', id: payment.vendorId }).done(function (data) {
                    if (data.results && data.results[0]) {
                        cacheVendor(data.results[0]);
                    }
                    updateVendorBalanceHint();
                });
                $('#payment-method').val(payment.paymentMethod).trigger('change');
                if (window.setPaSelect2Value) {
                    window.setPaSelect2Value($('#payment-bank-id'), payment.bankId || '', payment.bankName || '');
                } else {
                    $('#payment-bank-id').val(payment.bankId || '').trigger('change');
                }
                $('#cheque-number').val(payment.chequeNumber || '');
                $('#cheque-date').val(toInputDate(payment.chequeDate));
                $('#payment-notes').val(payment.notes || '');
                togglePaymentFields();
                updateVendorBalanceHint();
                updateAmountInWords();
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

        initDefaultDateFilters();
        $('#btn-apply-filter').on('click', reloadDataTable);
        $('#filter-from, #filter-to').on('change', reloadDataTable);

        if ($.fn.select2) {
            $('#payment-method').select2({
                theme: 'bootstrap-5',
                width: '100%',
                dropdownParent: $('#paymentModal'),
                minimumResultsForSearch: 0
            });
        }

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
        $('#payment-amount').on('input change', updateAmountInWords);

        $('#vendor-payments-table').on('click', '.btn-share-payment', function () {
            if (window.VendorPaymentShare) {
                window.VendorPaymentShare.open($(this).data('id'));
            }
        });

        $('#vendor-payments-table').on('click', '.btn-edit-payment', function () {
            openEditModal($(this).data('id'));
        });

        $('#vendor-payments-table').on('click', '.btn-delete-payment', function () {
            deletePayment($(this).data('id'));
        });
    });
})();
