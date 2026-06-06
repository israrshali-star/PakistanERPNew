(function () {
    'use strict';

    var customers = [];
    var items = [];
    var itemsById = {};
    var scenarios = [];
    var lineCounter = 0;

    function showError(message) {
        $('#invoice-form-error').removeClass('d-none').text(message);
    }

    function clearError() {
        $('#invoice-form-error').addClass('d-none').text('');
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
        $('#invoice-company-warning')
            .removeClass('d-none')
            .text(message || 'Select a company from the top navbar before creating an invoice.');
    }

    function getItem(itemId) {
        return itemsById[String(itemId)] || null;
    }

    function recalcTotals() {
        var subtotal = 0;
        var discount = 0;
        var tax = 0;

        $('#invoice-lines-body tr').each(function () {
            var $row = $(this);
            var qty = parseFloat($row.find('.line-qty').val()) || 0;
            var price = parseFloat($row.find('.line-price').val()) || 0;
            var taxRate = parseFloat($row.find('.line-tax').val()) || 0;
            var disc = parseFloat($row.find('.line-discount').val()) || 0;

            var lineSub = qty * price;
            var taxable = Math.max(0, lineSub - disc);
            var lineTax = taxable * taxRate / 100;
            var lineTotal = taxable + lineTax;

            $row.find('.line-total').text(formatCurrency(lineTotal));

            subtotal += lineSub;
            discount += disc;
            tax += lineTax;
        });

        $('#total-subtotal').text(formatCurrency(subtotal));
        $('#total-discount').text(formatCurrency(discount));
        $('#total-tax').text(formatCurrency(tax));
        $('#total-net').text(formatCurrency(subtotal - discount + tax));
    }

    function buildItemOptions(selectedId) {
        var html = '<option value="">— Select item —</option>';
        items.forEach(function (item) {
            var selected = String(item.id) === String(selectedId) ? ' selected' : '';
            html += '<option value="' + item.id + '"' + selected + '>' +
                item.itemCode + ' — ' + item.itemName + '</option>';
        });
        return html;
    }

    function initLineItemSelect($select) {
        if ($.fn.select2) {
            $select.select2({ theme: 'bootstrap-5', width: '100%', dropdownParent: $('#sales-invoice-form') });
        }
    }

    function applyItemToRow($row, item) {
        if (!item) {
            $row.find('.line-hs').val('');
            $row.find('.line-stack').val('');
            $row.find('.line-lot').val('');
            $row.find('.line-unit').text('—');
            $row.find('.line-price').val('0');
            $row.find('.line-tax').val('18');
            return;
        }

        $row.find('.line-hs').val(item.hsCode || '');
        $row.find('.line-stack').val(item.stackNo || '');
        $row.find('.line-lot').val(item.lotNo || '');
        $row.find('.line-unit').text(item.unitSymbol || 'PCS');
        $row.find('.line-price').val((item.saleRate || 0).toFixed(2));
        $row.find('.line-tax').val((item.defaultTaxRate || 18).toFixed(2));
        recalcTotals();
    }

    function addLine(prefill) {
        lineCounter += 1;
        var rowId = 'line-' + lineCounter;
        var $row = $(
            '<tr data-line-id="' + rowId + '">' +
            '<td><select class="form-select form-select-sm line-item" required>' + buildItemOptions(prefill && prefill.itemId) + '</select></td>' +
            '<td><input type="text" class="form-control form-control-sm line-hs" readonly /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-stack" maxlength="50" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-lot" maxlength="50" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-cartons" min="0" step="0.01" value="' + ((prefill && prefill.cartons) || 0) + '" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-qty" min="0.01" step="0.01" value="' + ((prefill && prefill.qty) || 1) + '" required /></td>' +
            '<td class="text-muted small line-unit">—</td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-price" min="0" step="0.01" value="0" required /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-tax" min="0" step="0.01" value="18" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-discount" min="0" step="0.01" value="0" /></td>' +
            '<td class="text-end text-currency line-total">0.00</td>' +
            '<td class="text-end"><button type="button" class="btn btn-link btn-sm text-danger p-0 btn-remove-line" title="Remove"><i class="fa-solid fa-xmark"></i></button></td>' +
            '</tr>'
        );

        $('#invoice-lines-body').append($row);

        var $select = $row.find('.line-item');
        initLineItemSelect($select);

        if (prefill && prefill.itemId) {
            $select.val(String(prefill.itemId)).trigger('change');
            applyItemToRow($row, getItem(prefill.itemId));
            if (prefill.price) {
                $row.find('.line-price').val(prefill.price);
            }
            if (prefill.tax) {
                $row.find('.line-tax').val(prefill.tax);
            }
        }

        recalcTotals();
    }

    function onCustomerChange() {
        var customerId = parseInt($('#customer-id').val(), 10);
        var customer = customers.find(function (c) { return c.id === customerId; });

        if (!customer) {
            return;
        }

        $('#scenario-id').val(String(customer.scenarioId)).trigger('change');
        $('#province-id').val(customer.provinceId ? String(customer.provinceId) : '').trigger('change');
        $('#buyer-address').val(customer.address || '');
        $('#buyer-ntn').val(customer.ntn || '');
        $('#buyer-cnic').val(customer.cnic || '');
        $('#invoice-type').val(String(customer.invoiceType));
    }

    function onItemChange($select) {
        var itemId = parseInt($select.val(), 10);
        applyItemToRow($select.closest('tr'), getItem(itemId));
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/sales-invoices/next-invoice-number'),
            $.getJSON('/api/sales-invoices/customers'),
            $.getJSON('/api/sales-invoices/items'),
            $.getJSON('/api/lookup/scenario-types'),
            $.getJSON('/api/lookup/provinces')
        ).then(function (numberRes, customersRes, itemsRes, scenariosRes, provincesRes) {
            $('#invoice-number').val(numberRes[0].invoiceNumber);
            customers = customersRes[0] || [];
            items = itemsRes[0] || [];
            itemsById = {};
            items.forEach(function (item) {
                itemsById[String(item.id)] = item;
            });
            scenarios = scenariosRes[0] || [];

            var $customer = $('#customer-id');
            $customer.find('option:not(:first)').remove();
            customers.forEach(function (c) {
                $customer.append($('<option></option>').val(c.id).text(c.buyerId + ' — ' + c.buyerName));
            });

            var $scenario = $('#scenario-id');
            $scenario.empty();
            scenarios.forEach(function (s) {
                $scenario.append($('<option></option>').val(s.id).text(s.code + ' — ' + (s.description || '')));
            });

            var $province = $('#province-id');
            $province.find('option:not(:first)').remove();
            (provincesRes[0] || []).forEach(function (p) {
                $province.append($('<option></option>').val(p.id).text(p.name));
            });

            if ($.fn.select2) {
                $('#customer-id, #scenario-id, #province-id').select2({ theme: 'bootstrap-5', width: '100%' });
            }

            if (items.length === 0) {
                $('#no-items-hint').removeClass('d-none');
            } else {
                $('#no-items-hint').addClass('d-none');
                if ($('#invoice-lines-body tr').length === 0) {
                    var first = items[0];
                    addLine({
                        itemId: first.id,
                        qty: 1,
                        cartons: 0,
                        price: first.saleRate,
                        tax: first.defaultTaxRate
                    });
                }
            }

            if (customers.length === 0) {
                showError('No active customers found. Add a customer under Sales → Customers first.');
            }
        });
    }

    function saveInvoice(e) {
        e.preventDefault();
        clearError();

        var customerId = parseInt($('#customer-id').val(), 10);
        var scenarioId = parseInt($('#scenario-id').val(), 10);
        var provinceVal = $('#province-id').val();
        var dateParts = $('#invoice-date').val().split('/');

        if (!customerId) {
            showError('Please select a customer.');
            return;
        }

        if (!scenarioId) {
            showError('Please select an FBR scenario.');
            return;
        }

        if (dateParts.length !== 3) {
            showError('Please enter a valid invoice date.');
            return;
        }

        var lines = [];
        var lineValid = true;

        $('#invoice-lines-body tr').each(function () {
            var $row = $(this);
            var itemId = parseInt($row.find('.line-item').val(), 10);

            if (!itemId) {
                lineValid = false;
                return false;
            }

            lines.push({
                itemId: itemId,
                stackNo: $row.find('.line-stack').val().trim() || null,
                lotNo: $row.find('.line-lot').val().trim() || null,
                cartons: parseFloat($row.find('.line-cartons').val()) || 0,
                quantity: parseFloat($row.find('.line-qty').val()) || 0,
                price: parseFloat($row.find('.line-price').val()) || 0,
                taxRate: parseFloat($row.find('.line-tax').val()) || 0,
                discount: parseFloat($row.find('.line-discount').val()) || 0
            });
        });

        if (!lineValid) {
            showError('Each line must include an item.');
            return;
        }

        if (lines.length === 0) {
            showError('Add at least one line item.');
            return;
        }

        var payload = {
            invoiceNumber: $('#invoice-number').val().trim(),
            customerId: customerId,
            invoiceDate: dateParts[2] + '-' + dateParts[1] + '-' + dateParts[0],
            invoiceType: parseInt($('#invoice-type').val(), 10),
            scenarioId: scenarioId,
            provinceId: provinceVal ? parseInt(provinceVal, 10) : null,
            buyerAddress: $('#buyer-address').val().trim() || null,
            buyerNTN: $('#buyer-ntn').val().trim() || null,
            buyerCNIC: $('#buyer-cnic').val().trim() || null,
            lines: lines
        };

        var $btn = $('#btn-save-invoice');
        $btn.prop('disabled', true);

        $.ajax({
            url: '/api/sales-invoices',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                var invoiceId = result && (result.invoiceId || result.InvoiceId);
                window.location.href = invoiceId
                    ? '/SalesInvoices/Details/' + invoiceId
                    : '/SalesInvoices';
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Failed to save invoice.'));
            })
            .always(function () {
                $btn.prop('disabled', false);
            });
    }

    $(function () {
        if (typeof flatpickr !== 'undefined') {
            flatpickr('#invoice-date', {
                dateFormat: 'd/m/Y',
                defaultDate: new Date(),
                allowInput: true
            });
        }

        ensureCompanySelected()
            .done(function () {
                loadLookups().fail(function () {
                    showError('Failed to load invoice data.');
                });
            })
            .fail(function () {
                showCompanyWarning();
            });

        $('#customer-id').on('change', onCustomerChange);
        $('#invoice-lines-body').on('change', '.line-item', function () {
            onItemChange($(this));
        });
        $('#invoice-lines-body').on('input', '.line-qty, .line-price, .line-tax, .line-discount', recalcTotals);
        $('#invoice-lines-body').on('click', '.btn-remove-line', function () {
            var $select = $(this).closest('tr').find('.line-item');
            if ($select.data('select2')) {
                $select.select2('destroy');
            }
            $(this).closest('tr').remove();
            recalcTotals();
        });

        $('#btn-add-line').on('click', function () {
            if (items.length === 0) {
                showError('No items available. Add items from Inventory → Items.');
                return;
            }
            addLine();
        });

        $('#sales-invoice-form').on('submit', saveInvoice);
    });
})();
