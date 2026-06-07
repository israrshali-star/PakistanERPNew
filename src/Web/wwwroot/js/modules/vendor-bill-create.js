(function () {
    'use strict';

    var vendors = [];
    var items = [];
    var itemsById = {};
    var lineCounter = 0;

    function showError(message) {
        $('#bill-form-error').removeClass('d-none').text(message);
    }

    function clearError() {
        $('#bill-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function ensureCompanySelected() {
        return $.getJSON('/api/company/current');
    }

    function showCompanyWarning(message) {
        $('#bill-company-warning')
            .removeClass('d-none')
            .text(message || 'Select a company from the top navbar before entering a bill.');
    }

    function getItem(itemId) {
        return itemsById[String(itemId)] || null;
    }

    function recalcTotals() {
        var subtotal = 0;
        var taxRate = parseFloat($('#tax-rate').val()) || 0;

        $('#bill-lines-body tr').each(function () {
            var $row = $(this);
            var qty = parseFloat($row.find('.line-qty').val()) || 0;
            var rate = parseFloat($row.find('.line-rate').val()) || 0;
            var amount = qty * rate;

            $row.find('.line-amount').text(formatCurrency(amount));
            subtotal += amount;
        });

        var tax = subtotal * taxRate / 100;
        var net = subtotal + tax;

        $('#total-subtotal').text(formatCurrency(subtotal));
        $('#total-tax').text(formatCurrency(tax));
        $('#total-net').text(formatCurrency(net));
    }

    function buildItemOptions(selectedId) {
        var html = '<option value="">— None —</option>';
        items.forEach(function (item) {
            var selected = String(item.id) === String(selectedId) ? ' selected' : '';
            html += '<option value="' + item.id + '"' + selected + '>' +
                item.itemCode + ' — ' + item.itemName + '</option>';
        });
        return html;
    }

    function initLineItemSelect($select) {
        if ($.fn.select2) {
            $select.select2({ theme: 'bootstrap-5', width: '100%', dropdownParent: $('#vendor-bill-form') });
        }
    }

    function applyItemToRow($row, item) {
        if (!item) {
            $row.find('.line-desc').val('');
            $row.find('.line-stack').val('');
            $row.find('.line-lot').val('');
            $row.find('.line-rate').val('0');
            return;
        }

        $row.find('.line-desc').val(item.description || item.itemName || '');
        $row.find('.line-stack').val(item.stackNo || '');
        $row.find('.line-lot').val(item.lotNo || '');
        $row.find('.line-rate').val((item.purchaseRate || 0).toFixed(2));
        recalcTotals();
    }

    function addLine(prefill) {
        lineCounter += 1;
        var rowId = 'line-' + lineCounter;
        var $row = $(
            '<tr data-line-id="' + rowId + '">' +
            '<td><select class="form-select form-select-sm line-item">' + buildItemOptions(prefill && prefill.itemId) + '</select></td>' +
            '<td><input type="text" class="form-control form-control-sm line-desc" maxlength="500" placeholder="Required if no item" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-stack" maxlength="50" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-lot" maxlength="50" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-cartons" min="0" step="0.01" value="' + ((prefill && prefill.cartons) || 0) + '" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-qty" min="0.01" step="0.01" value="' + ((prefill && prefill.qty) || 1) + '" required /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-rate" min="0" step="0.01" value="0" required /></td>' +
            '<td class="text-end text-currency line-amount">0.00</td>' +
            '<td class="text-end"><button type="button" class="btn btn-link btn-sm text-danger p-0 btn-remove-line" title="Remove"><i class="fa-solid fa-xmark"></i></button></td>' +
            '</tr>'
        );

        $('#bill-lines-body').append($row);

        var $select = $row.find('.line-item');
        initLineItemSelect($select);

        if (prefill && prefill.itemId) {
            $select.val(String(prefill.itemId)).trigger('change');
            applyItemToRow($row, getItem(prefill.itemId));
            if (prefill.rate) {
                $row.find('.line-rate').val(prefill.rate);
            }
        }

        recalcTotals();
    }

    function onVendorChange() {
        var vendorId = parseInt($('#vendor-id').val(), 10);
        var vendor = vendors.find(function (v) { return v.id === vendorId; });

        if (vendor && vendor.defaultTaxRate != null) {
            $('#tax-rate').val(vendor.defaultTaxRate);
            recalcTotals();
        }
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/vendor-bills/next-bill-number'),
            $.getJSON('/api/vendor-bills/vendors'),
            $.getJSON('/api/vendor-bills/items')
        ).then(function (numberRes, vendorsRes, itemsRes) {
            $('#bill-number').val(numberRes[0].billNumber);
            vendors = vendorsRes[0] || [];
            items = itemsRes[0] || [];
            itemsById = {};
            items.forEach(function (item) {
                itemsById[String(item.id)] = item;
            });

            var $vendor = $('#vendor-id');
            $vendor.find('option:not(:first)').remove();
            vendors.forEach(function (v) {
                $vendor.append($('<option></option>').val(v.id).text(v.vendorCode + ' — ' + v.vendorName));
            });

            if ($.fn.select2) {
                $vendor.select2({ theme: 'bootstrap-5', width: '100%' });
            }

            if (vendors.length === 0) {
                showError('No active vendors found. Add a vendor under Purchase → Vendors first.');
            }

            if ($('#bill-lines-body tr').length === 0) {
                if (items.length > 0) {
                    var first = items[0];
                    addLine({
                        itemId: first.id,
                        qty: 1,
                        cartons: 0,
                        rate: first.purchaseRate
                    });
                } else {
                    addLine();
                }
            }
        });
    }

    function saveBill(e) {
        e.preventDefault();
        clearError();

        var vendorId = parseInt($('#vendor-id').val(), 10);
        var billDate = $('#bill-date').val();

        if (!vendorId) {
            showError('Please select a vendor.');
            return;
        }

        if (!billDate) {
            showError('Please enter a bill date.');
            return;
        }

        var lines = [];
        var lineValid = true;

        $('#bill-lines-body tr').each(function () {
            var $row = $(this);
            var itemVal = $row.find('.line-item').val();
            var itemId = itemVal ? parseInt(itemVal, 10) : null;
            var description = $row.find('.line-desc').val().trim();

            if (!itemId && !description) {
                lineValid = false;
                return false;
            }

            lines.push({
                itemId: itemId,
                description: description || null,
                stackNo: $row.find('.line-stack').val().trim() || null,
                lotNo: $row.find('.line-lot').val().trim() || null,
                cartons: parseFloat($row.find('.line-cartons').val()) || 0,
                quantity: parseFloat($row.find('.line-qty').val()) || 0,
                rate: parseFloat($row.find('.line-rate').val()) || 0
            });
        });

        if (!lineValid) {
            showError('Each line must have an item or a description.');
            return;
        }

        if (lines.length === 0) {
            showError('Add at least one line item.');
            return;
        }

        var payload = {
            billNumber: $('#bill-number').val().trim(),
            vendorId: vendorId,
            billDate: billDate,
            refNo: $('#ref-no').val().trim() || null,
            taxRate: parseFloat($('#tax-rate').val()) || 0,
            lines: lines
        };

        var $btn = $('#vendor-bill-form button[type="submit"]');
        $btn.prop('disabled', true);

        $.ajax({
            url: '/api/vendor-bills',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function (result) {
                var billId = result && (result.billId || result.BillId);
                window.location.href = billId
                    ? '/VendorBills/Details/' + billId
                    : '/VendorBills';
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Failed to save bill.'));
            })
            .always(function () {
                $btn.prop('disabled', false);
            });
    }

    $(function () {
        var today = new Date();
        var isoDate = today.getFullYear() + '-' +
            String(today.getMonth() + 1).padStart(2, '0') + '-' +
            String(today.getDate()).padStart(2, '0');
        $('#bill-date').val(isoDate);

        ensureCompanySelected()
            .done(function () {
                loadLookups().fail(function () {
                    showError('Failed to load bill data.');
                });
            })
            .fail(function () {
                showCompanyWarning();
            });

        $('#vendor-id').on('change', onVendorChange);
        $('#tax-rate').on('input', recalcTotals);
        $('#bill-lines-body').on('change', '.line-item', function () {
            var itemId = parseInt($(this).val(), 10);
            applyItemToRow($(this).closest('tr'), getItem(itemId));
        });
        $('#bill-lines-body').on('input', '.line-qty, .line-rate', recalcTotals);
        $('#bill-lines-body').on('click', '.btn-remove-line', function () {
            var $select = $(this).closest('tr').find('.line-item');
            if ($select.data('select2')) {
                $select.select2('destroy');
            }
            $(this).closest('tr').remove();
            recalcTotals();
        });

        $('#btn-add-line').on('click', function () {
            addLine();
        });

        $('#vendor-bill-form').on('submit', saveBill);
    });
})();
