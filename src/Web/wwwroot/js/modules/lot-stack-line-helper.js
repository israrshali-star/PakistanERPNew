(function ($) {
    'use strict';

    var lotNumbers = [];
    var LOT_VALUE_SEP = '|';

    function escapeHtml(text) {
        return $('<div>').text(text == null ? '' : text).html();
    }

    function parseLotSelectValue(value) {
        if (!value || !String(value).trim()) {
            return { itemCode: null, lotNo: null, selectValue: '' };
        }

        var raw = String(value).trim();
        var sep = raw.indexOf(LOT_VALUE_SEP);
        if (sep > 0) {
            return {
                itemCode: raw.substring(0, sep).trim(),
                lotNo: raw.substring(sep + 1).trim(),
                selectValue: raw
            };
        }

        return { itemCode: null, lotNo: raw, selectValue: raw };
    }

    function buildLotSelectValue(itemCode, lotNo) {
        var code = itemCode ? String(itemCode).trim() : '';
        var lot = lotNo ? String(lotNo).trim() : '';
        if (code) {
            return code + LOT_VALUE_SEP + lot;
        }

        return lot;
    }

    function formatLotOptionLabel(option) {
        if (!option) {
            return '';
        }

        if (typeof option === 'string') {
            return option;
        }

        var code = option.itemCode || option.ItemCode || '';
        var lot = option.lotNo || option.LotNo || '';
        if (code && lot) {
            return code + ' + ' + lot;
        }

        if (code) {
            return code + ' + —';
        }

        return lot || code;
    }

    function formatQty(value) {
        var n = parseFloat(value);
        return isNaN(n) ? '0.00' : n.toFixed(2);
    }

    function hasFieldValue($input, numeric) {
        var value = $input.val();
        if (numeric) {
            var n = parseFloat(value);
            return !isNaN(n) && n !== 0;
        }

        return value != null && String(value).trim() !== '';
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function normalizeDetail(data) {
        if (!data) {
            return null;
        }
        return {
            lotNo: data.lotNo || data.LotNo,
            itemId: data.itemId || data.ItemId,
            itemCode: data.itemCode || data.ItemCode,
            itemName: data.itemName || data.ItemName,
            description: data.description || data.Description,
            hsCode: data.hsCode || data.HsCode,
            unitSymbol: data.unitSymbol || data.UnitSymbol,
            saleRate: data.saleRate != null ? data.saleRate : data.SaleRate,
            purchaseRate: data.purchaseRate != null ? data.purchaseRate : data.PurchaseRate,
            defaultStackNo: data.defaultStackNo || data.DefaultStackNo,
            stackNos: data.stackNos || data.StackNos || [],
            itemType: data.itemType != null ? data.itemType : data.ItemType
        };
    }

    var CARTAGE_ITEM_CODE = 'ITEM-0002';

    function isServiceItemType(itemType) {
        return parseInt(itemType, 10) === 2;
    }

    function isCartageItemCode(itemCode) {
        return String(itemCode || '').toUpperCase() === CARTAGE_ITEM_CODE;
    }

    function resolveTaxRate(options) {
        if (options && typeof options.getTaxRate === 'function') {
            var liveRate = parseFloat(options.getTaxRate());
            if (!isNaN(liveRate)) {
                return liveRate;
            }
        }

        var fallback = options ? options.defaultTaxRate : 0;
        var parsed = parseFloat(fallback);
        return isNaN(parsed) ? 0 : parsed;
    }

    function isNonTaxableDetail(detail) {
        return detail && (isServiceItemType(detail.itemType) || isCartageItemCode(detail.itemCode));
    }

    function resolveLineTaxRate(options, detail) {
        if (options.mode === 'sales' && isNonTaxableDetail(detail)) {
            return 0;
        }

        return resolveTaxRate(options);
    }

    function normalizeAvailability(data) {
        if (!data) {
            return null;
        }
        return {
            exists: data.exists != null ? data.exists : data.Exists,
            remainingWeight: data.remainingWeight != null ? data.remainingWeight : data.RemainingWeight,
            remainingCartons: data.remainingCartons != null ? data.remainingCartons : data.RemainingCartons,
            purchasedCartons: data.purchasedCartons != null ? data.purchasedCartons : data.PurchasedCartons,
            purchasedWeight: data.purchasedWeight != null ? data.purchasedWeight : data.PurchasedWeight,
            soldWeight: data.soldWeight != null ? data.soldWeight : data.SoldWeight
        };
    }

    function buildLotOptions(selectedValue) {
        var parsedSelected = parseLotSelectValue(selectedValue);
        var html = '<option value="">— Select item + lot —</option>';
        lotNumbers.forEach(function (option) {
            var value = typeof option === 'string'
                ? option
                : buildLotSelectValue(option.itemCode || option.ItemCode, option.lotNo || option.LotNo);
            var label = formatLotOptionLabel(option);
            var selected = '';

            if (parsedSelected.selectValue
                && value.toLowerCase() === parsedSelected.selectValue.toLowerCase()) {
                selected = ' selected';
            } else if (!parsedSelected.itemCode && parsedSelected.lotNo && typeof option !== 'string') {
                var lot = option.lotNo || option.LotNo || '';
                if (String(lot).toLowerCase() === parsedSelected.lotNo.toLowerCase()) {
                    selected = ' selected';
                }
            }

            html += '<option value="' + escapeHtml(value) + '"' + selected + '>' +
                escapeHtml(label) + '</option>';
        });
        return html;
    }

    function getRowLotSelection($row) {
        return parseLotSelectValue($row.find('.line-lot').val());
    }

    function updateStackDatalist($row, stackNos) {
        var listId = $row.data('stack-list-id');
        if (!listId) {
            listId = 'stack-list-' + ($row.data('line-id') || Date.now());
            $row.data('stack-list-id', listId);
            $row.find('.line-stack').attr('list', listId);
        }

        var $datalist = $('#' + listId);
        if (!$datalist.length) {
            $datalist = $('<datalist></datalist>').attr('id', listId).appendTo('body');
        }

        $datalist.empty();
        (stackNos || []).forEach(function (stack) {
            $datalist.append($('<option></option>').val(stack));
        });
    }

    function setStackHint($hint, cssClass, message) {
        $hint.removeClass('text-success text-danger text-muted').addClass(cssClass).text(message || '');
    }

    function buildAvailabilityMessage(data, $row, options) {
        if (!data || data.exists === false) {
            var lotSelection = getRowLotSelection($row);
            var lotPart = lotSelection.lotNo ? ' / lot ' + lotSelection.lotNo : '';
            return {
                css: 'text-danger',
                message: 'Stack ' + $row.find('.line-stack').val().trim() + lotPart + ' not found in purchases.'
            };
        }

        var qty = parseFloat($row.find('.line-qty').val()) || 0;
        var cartons = parseFloat($row.find('.line-cartons').val()) || 0;
        var msg = 'Available: ' + formatQty(data.remainingWeight) + ' weight';
        if ((data.purchasedCartons || 0) > 0) {
            msg += ', ' + formatQty(data.remainingCartons) + ' cartons';
        }
        if ((data.soldWeight || 0) > 0) {
            msg += ' (sold: ' + formatQty(data.soldWeight) + ')';
        }

        if (options.validateQty) {
            var exceedsWeight = qty > data.remainingWeight;
            var exceedsCartons = (data.purchasedCartons || 0) > 0 && cartons > data.remainingCartons;
            if (exceedsWeight || exceedsCartons) {
                var detail = msg;
                if (exceedsWeight) {
                    detail += ' — exceeds available weight';
                }
                if (exceedsCartons) {
                    detail += exceedsWeight ? ' and cartons' : ' — exceeds available cartons';
                }
                return { css: 'text-danger', message: detail };
            }
        }

        return { css: 'text-success', message: msg };
    }

    window.LotStackLine = {
        lotNumbers: lotNumbers,
        stockHintTimers: {},

        loadLotNumbers: function () {
            return $.getJSON('/api/lookup/lot-numbers')
                .then(function (data) {
                    lotNumbers = data || [];
                    window.LotStackLine.lotNumbers = lotNumbers;
                    return lotNumbers;
                });
        },

        initLotSelect: function ($select, dropdownParent) {
            if (!$.fn.select2) {
                return;
            }

            if (window.initPaSelect2) {
                window.initPaSelect2($select, {
                    width: 'resolve',
                    dropdownParent: dropdownParent,
                    tags: true,
                    placeholder: 'Item + lot'
                });
                return;
            }

            $select.select2({
                theme: 'bootstrap-5',
                width: 'resolve',
                dropdownParent: dropdownParent,
                tags: true,
                placeholder: 'Item + lot',
                minimumResultsForSearch: 0
            });
        },

        buildLotOptions: buildLotOptions,
        parseLotSelectValue: parseLotSelectValue,
        buildLotSelectValue: buildLotSelectValue,
        formatLotOptionLabel: formatLotOptionLabel,

        clearRow: function ($row, options) {
            $row.data('requires-stock', true);
            $row.data('is-taxable', true);
            $row.find('.line-item-id').val('');
            $row.find('.line-item-name').val('');
            $row.find('.line-desc').val('');
            $row.find('.line-stack').val('');
            updateStackDatalist($row, []);
            if (options.mode === 'sales') {
                $row.find('.line-carton-desc').val('');
                $row.find('.line-unit').text('—');
                $row.find('.line-price').val('0');
                $row.find('.line-tax').val(resolveTaxRate(options).toFixed(2));
            } else {
                $row.find('.line-rate').val('0');
            }
            setStackHint($row.find('.line-stock-hint'), 'text-muted', '');
        },

        applyLotDetail: function ($row, detail, options) {
            detail = normalizeDetail(detail);
            if (!detail) {
                this.clearRow($row, options);
                return;
            }

            var preserve = options && options.preserveLineFields;
            var requiresStock = !isServiceItemType(detail.itemType) && !isCartageItemCode(detail.itemCode);
            var isTaxable = !isNonTaxableDetail(detail);
            $row.data('requires-stock', requiresStock);
            $row.data('is-taxable', isTaxable);
            $row.find('.line-item-id').val(detail.itemId);
            $row.find('.line-item-name').val(detail.itemName);

            if (!preserve || !hasFieldValue($row.find('.line-desc'), false)) {
                $row.find('.line-desc').val(detail.description || detail.itemName || '');
            }

            if (!preserve || !hasFieldValue($row.find('.line-stack'), false)) {
                $row.find('.line-stack').val(requiresStock ? (detail.defaultStackNo || '') : '');
            }

            updateStackDatalist($row, requiresStock ? detail.stackNos : []);

            if (options.mode === 'sales') {
                $row.find('.line-unit').text(detail.unitSymbol || 'PCS');
                if (!preserve || !hasFieldValue($row.find('.line-price'), true)) {
                    $row.find('.line-price').val((detail.saleRate || 0).toFixed(2));
                }
                if (!preserve || !hasFieldValue($row.find('.line-tax'), true)) {
                    $row.find('.line-tax').val(resolveLineTaxRate(options, detail).toFixed(2));
                }
            } else if (!preserve || !hasFieldValue($row.find('.line-rate'), true)) {
                $row.find('.line-rate').val((detail.purchaseRate || 0).toFixed(2));
            }

            if (options.onApplied) {
                options.onApplied($row, detail);
            }

            this.updateStackHint($row, options);
        },

        fetchLotDetail: function (lotNo, itemCode) {
            var lot = lotNo ? String(lotNo).trim() : '';
            var code = itemCode ? String(itemCode).trim() : '';
            if (!lot && !code) {
                return $.Deferred().reject().promise();
            }

            var params = { lotNo: lot };
            if (code) {
                params.itemCode = code;
            }

            return $.getJSON('/api/lookup/lot-detail', params)
                .then(function (data) {
                    return normalizeDetail(data);
                });
        },

        onLotChange: function ($row, options) {
            var lotSelection = getRowLotSelection($row);
            var lotNo = lotSelection.lotNo;
            var itemCode = lotSelection.itemCode;
            if ((!lotNo || !String(lotNo).trim()) && (!itemCode || !String(itemCode).trim())) {
                this.clearRow($row, options);
                if (options.onRecalc) {
                    options.onRecalc();
                }
                return;
            }

            var self = this;
            this.fetchLotDetail(lotNo || '', itemCode)
                .done(function (detail) {
                    self.applyLotDetail($row, detail, options);
                    if (detail && detail.itemCode) {
                        var composite = buildLotSelectValue(detail.itemCode, detail.lotNo || '');
                        var $lot = $row.find('.line-lot');
                        if ($lot.val() !== composite) {
                            $lot.val(composite);
                            if ($lot.data('select2')) {
                                $lot.trigger('change.select2');
                            }
                        }
                    }
                    if (options.onRecalc) {
                        options.onRecalc();
                    }
                })
                .fail(function () {
                    $row.find('.line-item-id').val('');
                    $row.find('.line-item-name').val('');
                    setStackHint($row.find('.line-stock-hint'), 'text-muted',
                        'Lot not in database — enter item details manually.');
                    if (options.onRecalc) {
                        options.onRecalc();
                    }
                });
        },

        updateStackHint: function ($row, options) {
            var $hint = $row.find('.line-stock-hint');
            var rowId = $row.data('line-id');
            var itemId = parseInt($row.find('.line-item-id').val(), 10);
            var stackNo = $row.find('.line-stack').val().trim();
            var lotSelection = getRowLotSelection($row);
            var lotNo = lotSelection.lotNo;

            if ($row.data('requires-stock') === false) {
                setStackHint($hint, 'text-muted', 'Service/charge line — non-taxable, no stack or stock check.');
                return;
            }

            if (options.isStockCheckRequired && !options.isStockCheckRequired()) {
                setStackHint($hint, 'text-muted', '');
                return;
            }

            if (!itemId) {
                setStackHint($hint, 'text-muted', '');
                return;
            }

            if (!stackNo) {
                setStackHint($hint, 'text-muted', 'Enter stack no to see available weight and cartons.');
                return;
            }

            if (this.stockHintTimers[rowId]) {
                clearTimeout(this.stockHintTimers[rowId]);
            }

            setStackHint($hint, 'text-muted', 'Checking stack…');
            var self = this;

            this.stockHintTimers[rowId] = setTimeout(function () {
                $.getJSON('/api/lookup/stack-availability', {
                    itemId: itemId,
                    stackNo: stackNo,
                    lotNo: lotNo ? String(lotNo).trim() : undefined
                })
                    .done(function (data) {
                        var normalized = normalizeAvailability(data);
                        var result = buildAvailabilityMessage(normalized, $row, options);
                        setStackHint($hint, result.css, result.message);
                    })
                    .fail(function (xhr) {
                        setStackHint($hint, 'text-danger', getApiErrorMessage(xhr, 'Could not check stack availability.'));
                    });
            }, 300);
        },

        destroyLotSelect: function ($select) {
            if ($select.data('select2')) {
                $select.select2('destroy');
            }
        }
    };
})(jQuery);
