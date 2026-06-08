(function () {
    'use strict';

    var vendors = [];
    var items = [];
    var lineCounter = 0;
    var pendingAttachments = [];
    var MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
    var MAX_ATTACHMENT_COUNT = 10;
    var ALLOWED_ATTACHMENT_EXT = ['.jpg', '.jpeg', '.png', '.pdf'];

    function lineOptions() {
        return {
            mode: 'purchase',
            validateQty: false,
            onRecalc: recalcTotals
        };
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

    function addLine(prefill) {
        lineCounter += 1;
        var rowId = 'line-' + lineCounter;
        var lotOptions = window.LotStackLine.buildLotOptions(prefill && prefill.lotNo);
        var $row = $(
            '<tr data-line-id="' + rowId + '">' +
            '<td><select class="form-select form-select-sm line-lot">' + lotOptions + '</select></td>' +
            '<td><input type="hidden" class="line-item-id" />' +
            '<input type="text" class="form-control form-control-sm line-item-name" readonly placeholder="From lot" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-desc" maxlength="500" placeholder="Required if no item" /></td>' +
            '<td><input type="text" class="form-control form-control-sm line-stack" maxlength="50" />' +
            '<div class="line-stock-hint small mt-1"></div></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-cartons" min="0" step="0.01" value="' + ((prefill && prefill.cartons) || 0) + '" /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-qty" min="0.01" step="0.01" value="' + ((prefill && prefill.qty) || 1) + '" required /></td>' +
            '<td><input type="number" class="form-control form-control-sm text-end line-rate" min="0" step="0.01" value="0" required /></td>' +
            '<td class="text-end text-currency line-amount">0.00</td>' +
            '<td class="text-end"><button type="button" class="btn btn-link btn-sm text-danger p-0 btn-remove-line" title="Remove"><i class="fa-solid fa-xmark"></i></button></td>' +
            '</tr>'
        );

        $('#bill-lines-body').append($row);

        var $lotSelect = $row.find('.line-lot');
        window.LotStackLine.initLotSelect($lotSelect, $('#vendor-bill-form'));

        if (prefill && prefill.lotNo) {
            $lotSelect.val(prefill.lotNo).trigger('change');
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
            $.getJSON('/api/vendor-bills/items'),
            window.LotStackLine.loadLotNumbers()
        ).then(function (numberRes, vendorsRes, itemsRes) {
            $('#bill-number').val(numberRes[0].billNumber);
            vendors = vendorsRes[0] || [];
            items = itemsRes[0] || [];

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
                var firstLot = window.LotStackLine.lotNumbers[0];
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
            var lotNo = $row.find('.line-lot').val();
            var description = $row.find('.line-desc').val().trim();

            if (!lotNo || !String(lotNo).trim()) {
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
                lotNo: String(lotNo).trim(),
                cartons: parseFloat($row.find('.line-cartons').val()) || 0,
                quantity: parseFloat($row.find('.line-qty').val()) || 0,
                rate: parseFloat($row.find('.line-rate').val()) || 0
            });
        });

        if (!lineValid) {
            showError('Each line needs a lot number and either a linked item or a description.');
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
        $('#tax-rate').on('input', recalcTotals);
        $('#bill-lines-body').on('change', '.line-lot', function () {
            window.LotStackLine.onLotChange($(this).closest('tr'), lineOptions());
        });
        $('#bill-lines-body').on('input', '.line-qty, .line-rate', recalcTotals);
        $('#bill-lines-body').on('input', '.line-stack, .line-qty, .line-cartons', function () {
            window.LotStackLine.updateStackHint($(this).closest('tr'), lineOptions());
        });
        $('#bill-lines-body').on('click', '.btn-remove-line', function () {
            var $row = $(this).closest('tr');
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
