(function ($) {
    'use strict';

    var lotNumbers = [];

    function formatQty(value) {
        var n = parseFloat(value);
        return isNaN(n) ? '0.00' : n.toFixed(2);
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
            stackNos: data.stackNos || data.StackNos || []
        };
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

    function buildLotOptions(selectedLot) {
        var html = '<option value="">— Select lot —</option>';
        lotNumbers.forEach(function (lot) {
            var selected = selectedLot && String(lot).toLowerCase() === String(selectedLot).toLowerCase()
                ? ' selected' : '';
            html += '<option value="' + $('<div>').text(lot).html() + '"' + selected + '>' +
                $('<div>').text(lot).html() + '</option>';
        });
        return html;
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
            var lotNo = $row.find('.line-lot').val();
            var lotPart = lotNo ? ' / lot ' + lotNo : '';
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

            $select.select2({
                theme: 'bootstrap-5',
                width: '100%',
                dropdownParent: dropdownParent,
                tags: true,
                placeholder: 'Select or type lot no'
            });
        },

        buildLotOptions: buildLotOptions,

        clearRow: function ($row, options) {
            $row.find('.line-item-id').val('');
            $row.find('.line-item-name').val('');
            $row.find('.line-desc').val('');
            $row.find('.line-stack').val('');
            updateStackDatalist($row, []);
            if (options.mode === 'sales') {
                $row.find('.line-hs').val('');
                $row.find('.line-unit').text('—');
                $row.find('.line-price').val('0');
                $row.find('.line-tax').val((options.defaultTaxRate || 0).toFixed(2));
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

            $row.find('.line-item-id').val(detail.itemId);
            $row.find('.line-item-name').val(detail.itemName);
            $row.find('.line-desc').val(detail.description || detail.itemName || '');
            $row.find('.line-stack').val(detail.defaultStackNo || '');
            updateStackDatalist($row, detail.stackNos);

            if (options.mode === 'sales') {
                $row.find('.line-hs').val(detail.hsCode || '');
                $row.find('.line-unit').text(detail.unitSymbol || 'PCS');
                $row.find('.line-price').val((detail.saleRate || 0).toFixed(2));
                $row.find('.line-tax').val((options.defaultTaxRate || 0).toFixed(2));
            } else {
                $row.find('.line-rate').val((detail.purchaseRate || 0).toFixed(2));
            }

            if (options.onApplied) {
                options.onApplied($row, detail);
            }

            this.updateStackHint($row, options);
        },

        fetchLotDetail: function (lotNo) {
            if (!lotNo || !String(lotNo).trim()) {
                return $.Deferred().reject().promise();
            }

            return $.getJSON('/api/lookup/lot-detail', { lotNo: String(lotNo).trim() })
                .then(function (data) {
                    return normalizeDetail(data);
                });
        },

        onLotChange: function ($row, options) {
            var lotNo = $row.find('.line-lot').val();
            if (!lotNo || !String(lotNo).trim()) {
                this.clearRow($row, options);
                if (options.onRecalc) {
                    options.onRecalc();
                }
                return;
            }

            var self = this;
            this.fetchLotDetail(lotNo)
                .done(function (detail) {
                    self.applyLotDetail($row, detail, options);
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
            var lotNo = $row.find('.line-lot').val();

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
