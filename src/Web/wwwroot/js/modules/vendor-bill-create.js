(function () {
    'use strict';

    var vendors = [];
    var items = [];
    var warehouses = [];
    var lineCounter = 0;
    var pendingAttachments = [];
    var purchaseTaxSettings = {
        supportsPurchaseWithholdingTax: false,
        purchaseWithholdingTaxRate: 1,
        defaultIncomeTax236GRate: 0.10
    };
    var whtAmountManual = false;
    var it236gAmountManual = false;
    var MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
    var MAX_ATTACHMENT_COUNT = 10;
    var ALLOWED_ATTACHMENT_EXT = ['.jpg', '.jpeg', '.png', '.pdf'];

    function lineOptions() {
        return {
            mode: 'purchase',
            validateQty: false,
            onRecalc: recalcTotals,
            onApplied: function ($row) {
                recalcTotals();
            }
        };
    }

    function getBillTaxRate() {
        var rate = parseFloat($('#tax-rate').val());
        return isNaN(rate) || rate < 0 ? 0 : rate;
    }

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

    function hasFieldValue($input, numeric) {
        var value = $input.val();
        if (numeric) {
            var n = parseFloat(value);
            return !isNaN(n) && n !== 0;
        }

        return value != null && String(value).trim() !== '';
    }

    function updateTaxRateLabel() {
        // Tax rate is resolved on the server from vendor/company settings.
    }

    function getTaxableSubtotal() {
        var taxableSubtotal = 0;

        $('#bill-lines-body tr').each(function () {
            var $row = $(this);
            var qty = parseFloat($row.find('.line-qty').val()) || 0;
            var rate = parseFloat($row.find('.line-rate').val()) || 0;
            var amount = qty * rate;
            if ($row.data('is-taxable') !== false) {
                taxableSubtotal += amount;
            }
        });

        return taxableSubtotal;
    }

    function suggestPurchaseTaxAmounts(subtotal, taxAmount) {
        if (!purchaseTaxSettings.supportsPurchaseWithholdingTax) {
            return;
        }

        var taxableSubtotal = getTaxableSubtotal();
        var billGrossAmount = (subtotal || 0) + (taxAmount || 0);
        var whtRate = parseFloat($('#wht-rate').val()) || 0;
        var it236gRate = parseFloat($('#it236g-rate').val()) || 0;

        if (!whtAmountManual) {
            $('#wht-amount').val((billGrossAmount * whtRate / 100).toFixed(2));
        }

        if (!it236gAmountManual) {
            $('#it236g-amount').val((taxableSubtotal * it236gRate / 100).toFixed(2));
        }
    }

    function recalcTotals() {
        var subtotal = 0;
        var taxRate = getBillTaxRate();

        $('#bill-lines-body tr').each(function () {
            var $row = $(this);
            var qty = parseFloat($row.find('.line-qty').val()) || 0;
            var rate = parseFloat($row.find('.line-rate').val()) || 0;
            var amount = qty * rate;

            $row.find('.line-amount').text(formatCurrency(amount));
            subtotal += amount;
        });

        var tax = getTaxableSubtotal() * taxRate / 100;
        suggestPurchaseTaxAmounts(subtotal, tax);
        var wht = purchaseTaxSettings.supportsPurchaseWithholdingTax
            ? (parseFloat($('#wht-amount').val()) || 0)
            : 0;
        var it236g = purchaseTaxSettings.supportsPurchaseWithholdingTax
            ? (parseFloat($('#it236g-amount').val()) || 0)
            : 0;
        var net = subtotal + tax - wht - it236g;

        updateTaxRateLabel();
        $('#total-subtotal').text(formatCurrency(subtotal));
        $('#total-tax').text(formatCurrency(tax));
        $('#total-net').text(formatCurrency(net));
    }

    function lockBillLineRow($row) {
        $row.addClass('bill-line-locked').attr('data-locked', 'true');
        $row.find('input, select').prop('disabled', true);
        $row.find('.btn-remove-line')
            .prop('disabled', true)
            .removeClass('text-danger')
            .addClass('text-muted')
            .attr('title', 'Sold — cannot remove');
        $row.find('td:last').append(
            '<div class="small text-muted mt-1"><i class="fa-solid fa-lock me-1"></i>Sold</div>'
        );
    }

    function addLine(prefill) {
        lineCounter += 1;
        var rowId = 'line-' + lineCounter;
        var lotOptions = window.LotStackLine.buildLotOptions(prefill && prefill.lotNo);
        var $row = $(
            '<tr data-line-id="' + rowId + '">' +
            '<td class="line-lot-cell"><select class="form-select form-select-sm line-lot">' + lotOptions + '</select></td>' +
            '<td><input type="hidden" class="line-item-id" />' +
            '<input type="text" class="form-control form-control-xs line-item-name" readonly placeholder="Lot" /></td>' +
            '<td><input type="text" class="form-control form-control-xs line-desc" maxlength="500" placeholder="Required if no item" /></td>' +
            '<td><input type="text" class="form-control form-control-xs line-stack" maxlength="50" />' +
            '<div class="line-stock-hint small mt-1"></div></td>' +
            '<td><input type="number" class="form-control form-control-xs text-end line-cartons" min="0" step="0.01" value="' + ((prefill && prefill.cartons) || 0) + '" /></td>' +
            '<td><input type="number" class="form-control form-control-xs text-end line-qty" min="0.01" step="0.01" value="' + ((prefill && prefill.qty) || 1) + '" required /></td>' +
            '<td><input type="number" class="form-control form-control-xs text-end line-rate" min="0" step="0.01" value="0" required /></td>' +
            '<td class="text-end text-currency line-amount">0.00</td>' +
            '<td class="text-end"><button type="button" class="btn btn-link btn-sm text-danger p-0 btn-remove-line" title="Remove"><i class="fa-solid fa-xmark"></i></button></td>' +
            '</tr>'
        );

        $('#bill-lines-body').append($row);

        var $lotSelect = $row.find('.line-lot');
        window.LotStackLine.initLotSelect($lotSelect, $('#vendor-bill-form'));

        var lotSelectValue = prefill && (prefill.lotNo || prefill.lotValue);
        if (lotSelectValue) {
            $lotSelect.val(lotSelectValue);
            if (!prefill.skipLotTrigger) {
                $lotSelect.trigger('change');
            }
            if (prefill.rate != null) {
                $row.find('.line-rate').val(prefill.rate);
            }
        }

        recalcTotals();
    }

    function onVendorChange() {
        var vendorId = parseInt($('#vendor-id').val(), 10);
        var vendor = vendors.find(function (v) { return v.id === vendorId; });

        if (vendor && vendor.defaultTaxRate != null && vendor.defaultTaxRate > 0) {
            $('#tax-rate').val(vendor.defaultTaxRate);
            recalcTotals();
        }
    }

    function getEditId() {
        var raw = $('#vendor-bill-form').data('edit-id');
        var id = parseInt(raw, 10);
        return id > 0 ? id : null;
    }

    function loadEditBill() {
        var $data = $('#bill-edit-data');
        if (!$data.length) {
            return;
        }

        var bill = JSON.parse($data.text());
        $('#bill-number').val(bill.billNumber || bill.BillNumber);
        $('#bill-date').val((bill.billDate || bill.BillDate || '').substring(0, 10));
        $('#ref-no').val(bill.refNo || bill.RefNo || '');
        $('#vendor-id').val(String(bill.vendorId || bill.VendorId)).trigger('change');
        if (bill.warehouseId || bill.WarehouseId) {
            $('#warehouse-id').val(String(bill.warehouseId || bill.WarehouseId)).trigger('change');
        }

        var subTotal = bill.subTotal != null ? bill.subTotal : bill.SubTotal;
        var taxAmount = bill.taxAmount != null ? bill.taxAmount : bill.TaxAmount;
        if (subTotal > 0 && taxAmount >= 0) {
            $('#tax-rate').val(((taxAmount / subTotal) * 100).toFixed(2));
        }

        if (bill.withholdingTaxRate != null || bill.WithholdingTaxRate != null) {
            $('#wht-rate').val(bill.withholdingTaxRate != null ? bill.withholdingTaxRate : bill.WithholdingTaxRate);
            whtAmountManual = true;
        }
        if (bill.withholdingTaxAmount != null || bill.WithholdingTaxAmount != null) {
            $('#wht-amount').val(bill.withholdingTaxAmount != null ? bill.withholdingTaxAmount : bill.WithholdingTaxAmount);
            whtAmountManual = true;
        }
        if (bill.incomeTax236GRate != null || bill.IncomeTax236GRate != null) {
            $('#it236g-rate').val(bill.incomeTax236GRate != null ? bill.incomeTax236GRate : bill.IncomeTax236GRate);
            it236gAmountManual = true;
        }
        if (bill.incomeTax236GAmount != null || bill.IncomeTax236GAmount != null) {
            $('#it236g-amount').val(bill.incomeTax236GAmount != null ? bill.incomeTax236GAmount : bill.IncomeTax236GAmount);
            it236gAmountManual = true;
        }

        $('#bill-lines-body').empty();
        (bill.lines || bill.Lines || []).forEach(function (line) {
            var itemCode = line.itemCode || line.ItemCode || '';
            var lotNo = line.lotNo || line.LotNo || '';
            var lotValue = itemCode
                ? window.LotStackLine.buildLotSelectValue(itemCode, lotNo)
                : lotNo;
            addLine({
                lotNo: lotValue,
                qty: line.quantity != null ? line.quantity : line.Quantity,
                cartons: line.cartons != null ? line.cartons : line.Cartons,
                rate: line.rate != null ? line.rate : line.Rate,
                description: line.description || line.Description,
                stackNo: line.stackNo || line.StackNo,
                itemId: line.itemId || line.ItemId,
                skipLotTrigger: true
            });
            var $row = $('#bill-lines-body tr').last();
            if (line.description || line.Description) {
                $row.find('.line-desc').val(line.description || line.Description);
            }
            if (line.stackNo || line.StackNo) {
                $row.find('.line-stack').val(line.stackNo || line.StackNo);
            }
            if (line.rate != null || line.Rate != null) {
                $row.find('.line-rate').val(line.rate != null ? line.rate : line.Rate);
            }
            if (line.quantity != null || line.Quantity != null) {
                $row.find('.line-qty').val(line.quantity != null ? line.quantity : line.Quantity);
            }
            if (line.cartons != null || line.Cartons != null) {
                $row.find('.line-cartons').val(line.cartons != null ? line.cartons : line.Cartons);
            }
            if (line.itemId || line.ItemId) {
                $row.find('.line-item-id').val(line.itemId || line.ItemId);
            }
            if (!lotNo && itemCode) {
                $row.data('requires-stock', false);
                $row.data('is-taxable', false);
            }
            window.LotStackLine.onLotChange($row, $.extend({}, lineOptions(), { preserveLineFields: true }));

            if (line.isLocked === true || line.IsLocked === true) {
                lockBillLineRow($row);
            }
        });
        recalcTotals();
    }

    function applyPurchaseTaxSettings(purchaseTax) {
        purchaseTax = purchaseTax || {};
        purchaseTaxSettings.supportsPurchaseWithholdingTax =
            purchaseTax.supportsPurchaseWithholdingTax === true
            || purchaseTax.SupportsPurchaseWithholdingTax === true;
        purchaseTaxSettings.purchaseWithholdingTaxRate =
            purchaseTax.purchaseWithholdingTaxRate != null
                ? purchaseTax.purchaseWithholdingTaxRate
                : (purchaseTax.PurchaseWithholdingTaxRate || 1);
        purchaseTaxSettings.defaultIncomeTax236GRate =
            purchaseTax.defaultIncomeTax236GRate != null
                ? purchaseTax.defaultIncomeTax236GRate
                : (purchaseTax.DefaultIncomeTax236GRate || 0.10);

        var companyTaxRate = purchaseTax.defaultSalesTaxRate != null
            ? purchaseTax.defaultSalesTaxRate
            : (purchaseTax.DefaultSalesTaxRate || 18);
        if (!$('#tax-rate').val() || parseFloat($('#tax-rate').val()) <= 0) {
            $('#tax-rate').val(companyTaxRate);
        }

        if (purchaseTaxSettings.supportsPurchaseWithholdingTax) {
            var sectionLabel = purchaseTax.withholdingTaxSectionLabel
                || purchaseTax.WithholdingTaxSectionLabel
                || 'Payment for Goods u/s 153(1)(a)';
            var it236gLabel = purchaseTax.incomeTax236GSectionLabel
                || purchaseTax.IncomeTax236GSectionLabel
                || 'Income Tax u/s 236G';
            $('#wht-section-label').text('W/H Tax — ' + sectionLabel);
            $('#it236g-section-label').text(it236gLabel);
            $('#wht-nature').text(
                'Nature: ' + (purchaseTax.natureOfPayment || purchaseTax.NatureOfPayment || 'Withheld income tax adjustable') + ' (GL 12810)'
            );
            if (!$('#wht-rate').val() || parseFloat($('#wht-rate').val()) <= 0) {
                $('#wht-rate').val(purchaseTaxSettings.purchaseWithholdingTaxRate);
            }
            if (!$('#it236g-rate').val() || parseFloat($('#it236g-rate').val()) <= 0) {
                $('#it236g-rate').val(purchaseTaxSettings.defaultIncomeTax236GRate);
            }
            $('#purchase-tax-section').removeClass('d-none');
        } else {
            $('#purchase-tax-section').addClass('d-none');
        }
    }

    function loadLookups() {
        var editId = getEditId();
        var numberRequest = editId
            ? $.Deferred().resolve([{ billNumber: '' }]).promise()
            : $.getJSON('/api/vendor-bills/next-bill-number');

        $.getJSON('/api/vendor-bills/purchase-tax-settings')
            .done(applyPurchaseTaxSettings);

        return $.when(
            numberRequest,
            $.getJSON('/api/vendor-bills/vendors'),
            $.getJSON('/api/vendor-bills/items'),
            $.getJSON('/api/vendor-bills/warehouses'),
            $.getJSON('/api/vendor-bills/purchase-tax-settings'),
            window.LotStackLine.loadLotNumbers()
        ).then(function (numberRes, vendorsRes, itemsRes, warehousesRes, purchaseTaxRes) {
            if (!editId) {
                $('#bill-number').val(numberRes[0].billNumber || numberRes[0].BillNumber);
            }
            vendors = vendorsRes[0] || [];
            items = itemsRes[0] || [];
            warehouses = warehousesRes[0] || [];
            applyPurchaseTaxSettings(purchaseTaxRes[0] || {});

            var $vendor = $('#vendor-id');
            $vendor.find('option:not(:first)').remove();
            vendors.forEach(function (v) {
                $vendor.append($('<option></option>').val(v.id).text(v.vendorCode + ' — ' + v.vendorName));
            });

            if ($.fn.select2) {
                $vendor.select2({
                    theme: 'bootstrap-5',
                    width: '100%',
                    minimumResultsForSearch: 0
                });
            }

            var $warehouse = $('#warehouse-id');
            $warehouse.find('option:not(:first)').remove();
            warehouses.forEach(function (w) {
                $warehouse.append($('<option></option>').val(w.id).text(w.code + ' — ' + w.name));
            });

            if ($.fn.select2) {
                $warehouse.select2({
                    theme: 'bootstrap-5',
                    width: '100%',
                    minimumResultsForSearch: 0
                });
            }

            if (items.length === 0) {
                $('#no-items-hint').removeClass('d-none');
            } else {
                $('#no-items-hint').addClass('d-none');
            }

            if (vendors.length === 0) {
                showError('No active vendors found. Add a vendor under Purchase → Vendors first.');
            }

            if (editId) {
                loadEditBill();
            } else if ($('#bill-lines-body tr').length === 0) {
                var firstOption = window.LotStackLine.lotNumbers[0];
                var firstLot = firstOption
                    ? (typeof firstOption === 'string'
                        ? firstOption
                        : window.LotStackLine.buildLotSelectValue(
                            firstOption.itemCode || firstOption.ItemCode,
                            firstOption.lotNo || firstOption.LotNo))
                    : null;
                var firstItem = items[0];
                addLine({
                    lotNo: firstLot || (firstItem && firstItem.lotNo),
                    qty: 1,
                    cartons: 0,
                    rate: firstItem && firstItem.purchaseRate
                });
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
        return ALLOWED_ATTACHMENT_EXT.indexOf(getFileExtension(file.name)) >= 0;
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
        var input = $('#bill-attachments')[0];
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

    function uploadAttachments(billId) {
        if (!pendingAttachments.length) {
            return $.Deferred().resolve().promise();
        }

        var chain = $.Deferred().resolve().promise();
        pendingAttachments.forEach(function (file) {
            chain = chain.then(function () {
                var formData = new FormData();
                formData.append('file', file);
                return $.ajax({
                    url: '/api/vendor-bills/' + billId + '/attachments',
                    method: 'POST',
                    data: formData,
                    processData: false,
                    contentType: false
                });
            });
        });

        return chain;
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
            var itemId = parseInt($row.find('.line-item-id').val(), 10) || null;
            var lotSelection = window.LotStackLine.parseLotSelectValue($row.find('.line-lot').val());
            var lotNo = lotSelection.lotNo;
            var description = $row.find('.line-desc').val().trim();
            var requiresStock = $row.data('requires-stock') !== false;

            if (requiresStock && (!lotNo || !String(lotNo).trim())) {
                lineValid = false;
                return false;
            }

            if (!itemId && !description) {
                lineValid = false;
                return false;
            }

            lines.push({
                itemId: itemId,
                description: description || null,
                stackNo: $row.find('.line-stack').val().trim() || null,
                lotNo: lotNo && String(lotNo).trim() ? String(lotNo).trim() : null,
                cartons: parseFloat($row.find('.line-cartons').val()) || 0,
                quantity: parseFloat($row.find('.line-qty').val()) || 0,
                rate: parseFloat($row.find('.line-rate').val()) || 0
            });
        });

        if (!lineValid) {
            showError('Each goods line needs a lot number. Service/charge lines need only the service item selected.');
            return;
        }

        if (lines.length === 0) {
            showError('Add at least one line item.');
            return;
        }

        var payload = {
            billNumber: $('#bill-number').val().trim(),
            vendorId: vendorId,
            warehouseId: parseInt($('#warehouse-id').val(), 10) || null,
            billDate: billDate,
            refNo: $('#ref-no').val().trim() || null,
            taxRate: getBillTaxRate(),
            withholdingTaxRate: parseFloat($('#wht-rate').val()) || 0,
            withholdingTaxAmount: parseFloat($('#wht-amount').val()) || 0,
            incomeTax236GRate: parseFloat($('#it236g-rate').val()) || 0,
            incomeTax236GAmount: parseFloat($('#it236g-amount').val()) || 0,
            lines: lines
        };

        var editId = getEditId();
        var $btn = $('#btn-save-bill');
        $btn.prop('disabled', true);

        $.ajax({
            url: editId ? '/api/vendor-bills/' + editId : '/api/vendor-bills',
            method: editId ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function (result) {
                var billId = result && (result.billId || result.BillId);
                if (!billId) {
                    window.location.href = '/VendorBills';
                    return;
                }

                uploadAttachments(billId)
                    .done(function () {
                        window.location.href = '/VendorBills/Details/' + billId;
                    })
                    .fail(function (xhr) {
                        showError(getApiErrorMessage(xhr, 'Bill saved but some attachments failed to upload.'));
                        setTimeout(function () {
                            window.location.href = '/VendorBills/Details/' + billId;
                        }, 2000);
                    });
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
        $('#bill-lines-body').on('change', '.line-lot', function () {
            window.LotStackLine.onLotChange($(this).closest('tr'), lineOptions());
        });
        $('#bill-lines-body').on('input', '.line-qty, .line-rate', recalcTotals);
        $('#bill-lines-body').on('input', '.line-stack, .line-qty, .line-cartons', function () {
            window.LotStackLine.updateStackHint($(this).closest('tr'), lineOptions());
        });
        $('#wht-rate').on('input', function () {
            whtAmountManual = false;
            recalcTotals();
        });
        $('#wht-amount').on('input', function () {
            whtAmountManual = true;
            recalcTotals();
        });
        $('#it236g-rate').on('input', function () {
            it236gAmountManual = false;
            recalcTotals();
        });
        $('#it236g-amount').on('input', function () {
            it236gAmountManual = true;
            recalcTotals();
        });
        $('#bill-lines-body').on('click', '.btn-remove-line', function () {
            var $row = $(this).closest('tr');
            if ($row.data('locked') === true || $row.attr('data-locked') === 'true') {
                return;
            }
            var rowId = $row.data('line-id');
            if (rowId && window.LotStackLine.stockHintTimers[rowId]) {
                clearTimeout(window.LotStackLine.stockHintTimers[rowId]);
                delete window.LotStackLine.stockHintTimers[rowId];
            }
            window.LotStackLine.destroyLotSelect($row.find('.line-lot'));
            $row.remove();
            recalcTotals();
        });

        $('#btn-add-line').on('click', function () {
            addLine();
        });

        $('#vendor-bill-form').on('submit', saveBill);
        $('#bill-attachments').on('change', onAttachmentsSelected);
        $('#attachment-preview-list').on('click', '.btn-remove-attachment', function () {
            var index = parseInt($(this).data('index'), 10);
            pendingAttachments.splice(index, 1);
            renderAttachmentPreview();
        });
    });
})();
