(function () {
    'use strict';

    var customers = [];
    var items = [];
    var itemsById = {};
    var scenarios = [];
    var taxRates = { registered: 18, unregistered: 22 };
    var SN002_CODE = 'SN002';
    var lineCounter = 0;
    var stockHintTimers = {};
    var pendingAttachments = [];
    var MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
    var MAX_ATTACHMENT_COUNT = 10;
    var ALLOWED_ATTACHMENT_EXT = ['.jpg', '.jpeg', '.png', '.pdf'];
    var CREDIT_NOTE_TYPE = 3;

    function showError(message) {
        $('#invoice-form-error').removeClass('d-none').text(message);
    }

    function showStockAlert(message) {
        showError(message);
        if (message) {
            window.alert(message);
        }
    }

    function isStockCheckRequired() {
        return (parseInt($('#invoice-type').val(), 10) || 1) !== CREDIT_NOTE_TYPE;
    }

    function formatQty(value) {
        var n = parseFloat(value);
        if (isNaN(n)) {
            return '0.00';
        }
        return n.toFixed(2);
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

    function getSelectedScenarioCode() {
        var scenarioId = parseInt($('#scenario-id').val(), 10);
        var scenario = scenarios.find(function (s) { return s.id === scenarioId; });
        return scenario ? (scenario.code || '').toUpperCase() : '';
    }

    function getScenarioTaxRate() {
        return getSelectedScenarioCode() === SN002_CODE
            ? taxRates.unregistered
            : taxRates.registered;
    }

    function applyTaxRateToAllLines() {
        var rate = getScenarioTaxRate();
        $('#invoice-lines-body tr').each(function () {
            $(this).find('.line-tax').val(rate.toFixed(2));
        });
        recalcTotals();
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
            $row.find('.line-desc').val('');
            $row.find('.line-hs').val('');
            $row.find('.line-stack').val('');
            $row.find('.line-lot').val('');
            $row.find('.line-unit').text('—');
            $row.find('.line-price').val('0');
            $row.find('.line-tax').val(getScenarioTaxRate().toFixed(2));
            return;
        }

        $row.find('.line-desc').val(item.description || item.itemName || '');
        $row.find('.line-hs').val(item.hsCode || '');
        $row.find('.line-stack').val(item.stackNo || '');
        $row.find('.line-lot').val(item.lotNo || '');
        $row.find('.line-unit').text(item.unitSymbol || 'PCS');
        $row.find('.line-price').val((item.saleRate || 0).toFixed(2));
        $row.find('.line-tax').val(getScenarioTaxRate().toFixed(2));
        recalcTotals();
        updateStockHint($row);
    }

    function setStockHint($hint, cssClass, message) {
        $hint.removeClass('text-success text-danger text-muted').addClass(cssClass).text(message || '');
    }

    function updateStockHint($row) {
        var rowId = $row.data('line-id');
        var $hint = $row.find('.line-stock-hint');

        if (!isStockCheckRequired()) {
            setStockHint($hint, 'text-muted', '');
            return;
        }

        var itemId = parseInt($row.find('.line-item').val(), 10);
        var stackNo = $row.find('.line-stack').val().trim();
        var lotNo = $row.find('.line-lot').val().trim();

        if (!itemId) {
            setStockHint($hint, 'text-muted', '');
            return;
        }

        if (!stackNo) {
            setStockHint($hint, 'text-danger', 'Stack number is required.');
            return;
        }

        if (stockHintTimers[rowId]) {
            clearTimeout(stockHintTimers[rowId]);
        }

        setStockHint($hint, 'text-muted', 'Checking stock…');

        stockHintTimers[rowId] = setTimeout(function () {
            $.getJSON('/api/sales-invoices/stack-availability', {
                itemId: itemId,
                stackNo: stackNo,
                lotNo: lotNo || undefined
            })
                .done(function (data) {
                    if (!data || data.exists === false) {
                        var lotPart = lotNo ? ' / lot ' + lotNo : '';
                        setStockHint($hint, 'text-danger', 'Stack ' + stackNo + lotPart + ' not found in purchases.');
                        return;
                    }

                    var qty = parseFloat($row.find('.line-qty').val()) || 0;
                    var cartons = parseFloat($row.find('.line-cartons').val()) || 0;
                    var remainingWeight = data.remainingWeight != null ? data.remainingWeight : data.RemainingWeight;
                    var remainingCartons = data.remainingCartons != null ? data.remainingCartons : data.RemainingCartons;
                    var purchasedCartons = data.purchasedCartons != null ? data.purchasedCartons : data.PurchasedCartons;
                    var soldWeight = data.soldWeight != null ? data.soldWeight : data.SoldWeight;

                    var msg = 'Available: ' + formatQty(remainingWeight) + ' weight';
                    if ((purchasedCartons || 0) > 0) {
                        msg += ', ' + formatQty(remainingCartons) + ' cartons';
                    }
                    if ((soldWeight || 0) > 0) {
                        msg += ' (sold: ' + formatQty(soldWeight) + ')';
                    }

                    var exceedsWeight = qty > remainingWeight;
                    var exceedsCartons = (purchasedCartons || 0) > 0 && cartons > remainingCartons;

                    if (exceedsWeight || exceedsCartons) {
                        var detail = msg;
                        if (exceedsWeight) {
                            detail += ' — exceeds available weight';
                        }
                        if (exceedsCartons) {
                            detail += exceedsWeight ? ' and cartons' : ' — exceeds available cartons';
                        }
                        setStockHint($hint, 'text-danger', detail);
                    } else {
                        setStockHint($hint, 'text-success', msg);
                    }
                })
                .fail(function (xhr) {
                    setStockHint($hint, 'text-danger', getApiErrorMessage(xhr, 'Could not check stack availability.'));
                });
        }, 300);
    }

    function refreshAllStockHints() {
        $('#invoice-lines-body tr').each(function () {
            updateStockHint($(this));
        });
    }

    function addLine(prefill) {
        lineCounter += 1;
        var rowId = 'line-' + lineCounter;
        var $row = $(
            '<tr data-line-id="' + rowId + '">' +
            '<td><select class="form-select form-select-sm line-item" required>' + buildItemOptions(prefill && prefill.itemId) + '</select></td>' +
            '<td><input type="text" class="form-control form-control-sm line-desc" maxlength="500" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-hs" readonly /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-stack" maxlength="50" />' +
            '<div class="line-stock-hint small mt-1"></div></td>' +
            '<td><input type="text" class="form-control form-control-sm line-lot" maxlength="50" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-cartons" min="0" step="0.01" value="' + ((prefill && prefill.cartons) || 0) + '" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-qty" min="0.01" step="0.01" value="' + ((prefill && prefill.qty) || 1) + '" required /></td>' +
            '<td class="text-muted small line-unit">—</td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-price" min="0" step="0.01" value="0" required /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-tax" min="0" step="0.01" value="' + getScenarioTaxRate().toFixed(2) + '" /></td>' +
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
        updateStockHint($row);
    }

    function onScenarioChange() {
        applyTaxRateToAllLines();
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
            $.getJSON('/api/sales-invoices/tax-rates'),
            $.getJSON('/api/lookup/scenario-types'),
            $.getJSON('/api/lookup/provinces')
        ).then(function (numberRes, customersRes, itemsRes, taxRatesRes, scenariosRes, provincesRes) {
            $('#invoice-number').val(numberRes[0].invoiceNumber);
            customers = customersRes[0] || [];
            items = itemsRes[0] || [];
            itemsById = {};
            items.forEach(function (item) {
                itemsById[String(item.id)] = item;
            });
            scenarios = scenariosRes[0] || [];
            if (taxRatesRes[0]) {
                taxRates.registered = taxRatesRes[0].registeredSalesTaxRate != null
                    ? taxRatesRes[0].registeredSalesTaxRate
                    : (taxRatesRes[0].RegisteredSalesTaxRate || 18);
                taxRates.unregistered = taxRatesRes[0].unregisteredSalesTaxRate != null
                    ? taxRatesRes[0].unregisteredSalesTaxRate
                    : (taxRatesRes[0].UnregisteredSalesTaxRate || 22);
            }

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
                        tax: getScenarioTaxRate()
                    });
                }
            }

            if (customers.length === 0) {
                showError('No active customers found. Add a customer under Sales → Customers first.');
            }
        });
    }

    function formatFileSize(bytes) {
        if (bytes < 1024) {
            return bytes + ' B';
        }
        return (bytes / 1024).toFixed(1) + ' KB';
    }

    function getFileExtension(name) {
        var dot = name.lastIndexOf('.');
        return dot >= 0 ? name.substring(dot).toLowerCase() : '';
    }

    function isAllowedAttachment(file) {
        var ext = getFileExtension(file.name);
        return ALLOWED_ATTACHMENT_EXT.indexOf(ext) >= 0;
    }

    function renderAttachmentPreview() {
        var $list = $('#attachment-preview-list');
        $list.empty();

        pendingAttachments.forEach(function (file, index) {
            $list.append(
                '<li class="list-group-item d-flex justify-content-between align-items-center px-0">' +
                '<span><i class="fa-solid fa-file me-1"></i>' + $('<div>').text(file.name).html() +
                ' <span class="text-muted small">(' + formatFileSize(file.size) + ')</span></span>' +
                '<button type="button" class="btn btn-link btn-sm text-danger p-0 btn-remove-attachment" data-index="' + index + '">' +
                '<i class="fa-solid fa-xmark"></i></button></li>'
            );
        });
    }

    function onAttachmentsSelected() {
        clearError();
        var input = $('#invoice-attachments')[0];
        if (!input || !input.files) {
            return;
        }

        Array.prototype.forEach.call(input.files, function (file) {
            if (pendingAttachments.length >= MAX_ATTACHMENT_COUNT) {
                showError('Maximum ' + MAX_ATTACHMENT_COUNT + ' attachments allowed.');
                return;
            }

            if (!isAllowedAttachment(file)) {
                showError('Only JPG, PNG, and PDF files are allowed.');
                return;
            }

            if (file.size > MAX_ATTACHMENT_BYTES) {
                showError('Each attachment must be 10 MB or smaller.');
                return;
            }

            pendingAttachments.push(file);
        });

        input.value = '';
        renderAttachmentPreview();
    }

    function uploadAttachments(invoiceId) {
        if (!pendingAttachments.length) {
            return $.Deferred().resolve().promise();
        }

        var chain = $.Deferred().resolve().promise();
        pendingAttachments.forEach(function (file) {
            chain = chain.then(function () {
                var formData = new FormData();
                formData.append('file', file);
                return $.ajax({
                    url: '/api/sales-invoices/' + invoiceId + '/attachments',
                    method: 'POST',
                    data: formData,
                    processData: false,
                    contentType: false
                });
            });
        });

        return chain;
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
                productDescription: $row.find('.line-desc').val().trim() || null,
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
            .done(function (result) {
                if (result && result.success === false) {
                    var msg = result.message || 'Failed to save invoice.';
                    if (/stack|weight|carton|purchase/i.test(msg)) {
                        showStockAlert(msg);
                    } else {
                        showError(msg);
                    }
                    return;
                }

                var invoiceId = result && (result.invoiceId || result.InvoiceId);
                if (!invoiceId) {
                    window.location.href = '/SalesInvoices';
                    return;
                }

                uploadAttachments(invoiceId)
                    .done(function () {
                        window.location.href = '/SalesInvoices/Details/' + invoiceId;
                    })
                    .fail(function (xhr) {
                        showError(getApiErrorMessage(xhr, 'Invoice saved but some attachments failed to upload.'));
                        setTimeout(function () {
                            window.location.href = '/SalesInvoices/Details/' + invoiceId;
                        }, 2000);
                    });
            })
            .fail(function (xhr) {
                var msg = getApiErrorMessage(xhr, 'Failed to save invoice.');
                if (/stack|weight|carton|purchase/i.test(msg)) {
                    showStockAlert(msg);
                } else {
                    showError(msg);
                }
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
        $('#scenario-id').on('change', onScenarioChange);
        $('#invoice-type').on('change', refreshAllStockHints);
        $('#invoice-lines-body').on('change', '.line-item', function () {
            onItemChange($(this));
        });
        $('#invoice-lines-body').on('input', '.line-qty, .line-price, .line-tax, .line-discount', recalcTotals);
        $('#invoice-lines-body').on('input', '.line-stack, .line-lot, .line-qty, .line-cartons', function () {
            updateStockHint($(this).closest('tr'));
        });
        $('#invoice-lines-body').on('click', '.btn-remove-line', function () {
            var $row = $(this).closest('tr');
            var rowId = $row.data('line-id');
            if (rowId && stockHintTimers[rowId]) {
                clearTimeout(stockHintTimers[rowId]);
                delete stockHintTimers[rowId];
            }
            var $select = $row.find('.line-item');
            if ($select.data('select2')) {
                $select.select2('destroy');
            }
            $row.remove();
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
        $('#invoice-attachments').on('change', onAttachmentsSelected);
        $('#attachment-preview-list').on('click', '.btn-remove-attachment', function () {
            var index = parseInt($(this).data('index'), 10);
            pendingAttachments.splice(index, 1);
            renderAttachmentPreview();
        });
    });
})();
