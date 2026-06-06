(function () {
    'use strict';

    var stockModal = null;
    var dataTable = null;
    var canCreate = false;
    var items = [];

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatMoney(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function toInputDate(value) {
        var d = value ? new Date(value) : new Date();
        return isNaN(d.getTime()) ? '' : d.toISOString().slice(0, 10);
    }

    function showFormError(message) {
        $('#stock-txn-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#stock-txn-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function updateItemStockHint() {
        var itemId = parseInt($('#txn-item-id').val(), 10) || 0;
        var item = items.find(function (i) { return i.id === itemId; });
        if (item) {
            $('#txn-item-stock').text('Current stock: ' + formatMoney(item.currentStock) + ' ' + (item.unitSymbol || ''));
            if (!$('#txn-unit-cost').val() || $('#txn-unit-cost').val() === '0') {
                // leave unit cost for user
            }
        } else {
            $('#txn-item-stock').text('');
        }
    }

    function updateQtyHint() {
        var type = parseInt($('#txn-type').val(), 10) || 1;
        if (type === 3) {
            $('#txn-qty-hint').text('Use negative quantity to reduce stock (adjustment).');
        } else {
            $('#txn-qty-hint').text('Enter a positive quantity.');
        }
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/inventory-transactions/items'),
            $.getJSON('/api/inventory-transactions/warehouses')
        ).then(function (itemsRes, warehousesRes) {
            items = itemsRes[0] || [];

            var $item = $('#txn-item-id');
            $item.find('option:not(:first)').remove();
            items.forEach(function (i) {
                $item.append(
                    $('<option></option>')
                        .val(i.id)
                        .text(i.itemCode + ' — ' + i.itemName)
                );
            });

            var $warehouse = $('#txn-warehouse-id');
            $warehouse.find('option:not(:first)').remove();
            (warehousesRes[0] || []).forEach(function (w) {
                $warehouse.append(
                    $('<option></option>')
                        .val(w.id)
                        .text(w.code + ' — ' + w.name)
                );
            });

            if ((warehousesRes[0] || []).length === 0) {
                $('#stock-company-warning')
                    .removeClass('d-none')
                    .text('No active warehouses found. Create a warehouse first under Inventory → Warehouses.');
            }
        });
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#stock-transactions-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/inventory-transactions/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#stock-company-warning')
                            .removeClass('d-none')
                            .text(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[1, 'desc']],
            pageLength: 25,
            columns: [
                {
                    data: 'referenceNo',
                    render: function (d) { return d ? '<code>' + escapeHtml(d) + '</code>' : '—'; }
                },
                {
                    data: 'transactionDate',
                    render: function (d) {
                        var dt = new Date(d);
                        return isNaN(dt.getTime()) ? d : dt.toLocaleDateString('en-GB');
                    }
                },
                { data: 'transactionType' },
                {
                    data: 'itemCode',
                    render: function (d, type, row) {
                        return '<code>' + escapeHtml(d) + '</code> ' + escapeHtml(row.itemName);
                    }
                },
                { data: 'warehouseName' },
                {
                    data: 'quantity',
                    className: 'text-end',
                    render: function (d) { return formatMoney(d); }
                },
                {
                    data: 'totalCost',
                    className: 'text-end text-currency',
                    render: function (d) { return formatCurrency(d); }
                }
            ],
            language: { emptyTable: 'No stock transactions yet.' }
        });
    }

    function resetForm() {
        $('#stock-txn-form')[0].reset();
        $('#txn-date').val(toInputDate(new Date()));
        $('#txn-type').val('1');
        $('#txn-item-id, #txn-warehouse-id').val('').trigger('change');
        $('#txn-unit-cost').val('0');
        updateQtyHint();
        updateItemStockHint();
        $.getJSON('/api/inventory-transactions/next-reference')
            .done(function (res) { $('#txn-reference').val(res.referenceNo); });
    }

    function openCreateModal() {
        clearFormError();
        resetForm();
        stockModal.show();
    }

    function saveTransaction(e) {
        e.preventDefault();
        clearFormError();

        var payload = {
            itemId: parseInt($('#txn-item-id').val(), 10) || 0,
            warehouseId: parseInt($('#txn-warehouse-id').val(), 10) || 0,
            transactionType: parseInt($('#txn-type').val(), 10) || 1,
            stackNo: $('#txn-stack-no').val().trim() || null,
            lotNo: $('#txn-lot-no').val().trim() || null,
            quantity: parseFloat($('#txn-quantity').val()) || 0,
            unitCost: parseFloat($('#txn-unit-cost').val()) || 0,
            transactionDate: $('#txn-date').val(),
            referenceNo: $('#txn-reference').val().trim() || null,
            notes: $('#txn-notes').val().trim() || null
        };

        $.ajax({
            url: '/api/inventory-transactions',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                stockModal.hide();
                dataTable.ajax.reload(null, false);
                loadLookups();
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save transaction.'));
            });
    }

    $(function () {
        canCreate = $('#stock-permissions').data('can-create') === true;
        if (!canCreate) {
            $('#btn-add-stock-txn').remove();
        }

        stockModal = new bootstrap.Modal(document.getElementById('stockTxnModal'));

        $('#txn-item-id, #txn-warehouse-id').select2({
            theme: 'bootstrap-5',
            width: '100%',
            dropdownParent: $('#stockTxnModal')
        });

        $.getJSON('/api/company/current')
            .done(function () {
                $('#stock-company-warning').addClass('d-none');
                loadLookups().always(initDataTable);
            })
            .fail(function () {
                $('#stock-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar.');
            });

        $('#btn-add-stock-txn').on('click', openCreateModal);
        $('#btn-generate-txn-ref').on('click', function () {
            $.getJSON('/api/inventory-transactions/next-reference')
                .done(function (res) { $('#txn-reference').val(res.referenceNo); });
        });
        $('#txn-type').on('change', updateQtyHint);
        $('#txn-item-id').on('change', updateItemStockHint);
        $('#stock-txn-form').on('submit', saveTransaction);
    });
})();
