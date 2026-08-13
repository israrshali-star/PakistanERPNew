// Global ERP helpers — amount only; pair with .text-currency for PKR prefix
window.formatCurrency = function (value) {
    const num = parseFloat(value) || 0;
    return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
};

(function ($) {
    'use strict';

    if (!$.fn.select2) {
        return;
    }

    function containsMatcher(params, data) {
        if ($.trim(params.term) === '') {
            return data;
        }

        if (typeof data.text === 'undefined') {
            return null;
        }

        if (data.text.toLowerCase().indexOf(params.term.toLowerCase()) > -1) {
            return data;
        }

        return null;
    }

    var baseOptions = {
        theme: 'bootstrap-5',
        width: '100%',
        minimumResultsForSearch: 0,
        matcher: containsMatcher
    };

    $.fn.select2.defaults.set('theme', baseOptions.theme);
    $.fn.select2.defaults.set('width', baseOptions.width);
    $.fn.select2.defaults.set('minimumResultsForSearch', baseOptions.minimumResultsForSearch);
    $.fn.select2.defaults.set('matcher', baseOptions.matcher);

    window.initPaSelect2 = function ($elements, options) {
        return $elements.each(function () {
            var $el = $(this);
            if ($el.data('select2')) {
                $el.select2('destroy');
            }

            var config = $.extend({}, baseOptions, options || {});
            var dropdownParent = $el.data('dropdown-parent');
            if (dropdownParent) {
                config.dropdownParent = $(dropdownParent);
            }

            $el.select2(config);
        });
    };

    window.setPaSelect2Value = function ($select, id, text, options) {
        options = options || {};
        if (!$select || !$select.length) {
            return $select;
        }

        if (id == null || id === '') {
            $select.val(null);
            if (options.trigger !== false) {
                $select.trigger('change');
            }
            return $select;
        }

        var value = String(id);
        var hasOption = $select.find('option').filter(function () {
            return String(this.value) === value;
        }).length > 0;

        if (!hasOption) {
            $select.append(new Option(text || value, value, true, true));
        }

        $select.val(value);
        if (options.trigger !== false) {
            $select.trigger('change');
        }
        return $select;
    };

    window.initPaAjaxSelect2 = function ($elements, options) {
        options = options || {};
        if (!$.fn.select2) {
            return $elements;
        }

        return $elements.each(function () {
            var $el = $(this);
            if ($el.data('select2')) {
                $el.select2('destroy');
            }

            var entity = options.entity || $el.data('search-entity');
            var config = $.extend({}, baseOptions, {
                minimumInputLength: options.minimumInputLength != null ? options.minimumInputLength : 0,
                placeholder: options.placeholder || $el.data('placeholder') || 'Type to search',
                allowClear: options.allowClear !== false,
                ajax: {
                    url: options.url || '/api/lookup/search',
                    dataType: 'json',
                    delay: options.delay || 250,
                    data: function (params) {
                        var data = {
                            entity: entity,
                            q: params.term || '',
                            limit: options.limit || 20
                        };
                        if (typeof options.extraData === 'function') {
                            $.extend(data, options.extraData());
                        } else if (options.extraData) {
                            $.extend(data, options.extraData);
                        }
                        return data;
                    },
                    processResults: function (data) {
                        var items = data && data.results ? data.results : (data || []);
                        if (typeof options.mapResults === 'function') {
                            return { results: options.mapResults(items) };
                        }

                        var hasGroups = items.some(function (item) { return item.group; });
                        if (!hasGroups) {
                            return { results: items };
                        }

                        var groups = {};
                        var order = [];
                        items.forEach(function (item) {
                            var group = item.group || 'Other';
                            if (!groups[group]) {
                                groups[group] = [];
                                order.push(group);
                            }
                            groups[group].push(item);
                        });

                        return {
                            results: order.map(function (group) {
                                return { text: group, children: groups[group] };
                            })
                        };
                    },
                    cache: true
                }
            }, options.select2 || {});

            if (options.dropdownParent) {
                config.dropdownParent = $(options.dropdownParent);
            } else if ($el.data('dropdown-parent')) {
                config.dropdownParent = $($el.data('dropdown-parent'));
            }

            if (options.tags) {
                config.tags = true;
            }
            if (options.width) {
                config.width = options.width;
            }

            $el.select2(config);

            if (typeof options.onSelect === 'function') {
                $el.off('select2:select.paAjax').on('select2:select.paAjax', function (e) {
                    options.onSelect(e.params.data, $el);
                });
            }
        });
    };

    window.initPaAjaxTypeahead = function ($inputs, options) {
        options = options || {};
        return $inputs.each(function () {
            var $input = $(this);
            if ($input.data('pa-typeahead')) {
                return;
            }

            $input.data('pa-typeahead', true);
            if (!$input.parent().hasClass('pa-typeahead')) {
                $input.wrap('<div class="pa-typeahead"></div>');
            }

            var $wrap = $input.parent();
            var $menu = $('<div class="pa-typeahead-menu d-none" role="listbox"></div>');
            $wrap.append($menu);

            var timer = null;

            function hideMenu() {
                $menu.addClass('d-none').empty();
            }

            function renderItems(items) {
                $menu.empty();
                if (!items.length) {
                    $menu.append('<div class="pa-typeahead-empty">No matches</div>').removeClass('d-none');
                    return;
                }

                items.forEach(function (item) {
                    $menu.append(
                        $('<button type="button" class="pa-typeahead-item"></button>')
                            .text(item.text)
                            .data('item', item)
                    );
                });
                $menu.removeClass('d-none');
            }

            function search(term) {
                $.getJSON(options.url || '/api/lookup/search', {
                    entity: options.entity,
                    q: term,
                    limit: options.limit || 15
                }).done(function (data) {
                    renderItems((data && data.results) || []);
                });
            }

            $input.on('input', function () {
                var term = String($input.val() || '').trim();
                window.clearTimeout(timer);
                if (term.length < (options.minChars || 1)) {
                    hideMenu();
                    return;
                }

                timer = window.setTimeout(function () {
                    search(term);
                }, options.delay || 250);
            });

            $input.on('keydown', function (e) {
                if (e.key === 'Escape') {
                    hideMenu();
                    return;
                }

                if (e.key === 'Enter' && !$menu.hasClass('d-none') && options.selectOnEnter) {
                    var $first = $menu.find('.pa-typeahead-item').first();
                    if ($first.length) {
                        e.preventDefault();
                        $first.trigger('mousedown');
                    }
                }
            });

            $menu.on('mousedown', '.pa-typeahead-item', function (e) {
                e.preventDefault();
                var item = $(this).data('item');
                $input.val(typeof options.pickValue === 'function' ? options.pickValue(item) : item.text);
                hideMenu();
                if (typeof options.onSelect === 'function') {
                    options.onSelect(item, $input);
                }
            });

            $input.on('blur', function () {
                window.setTimeout(hideMenu, 150);
            });
        });
    };

    function openAndTypeSearch($select, character) {
        var instance = $select.data('select2');
        if (!instance) {
            return;
        }

        var wasOpen = instance.isOpen();
        if (!wasOpen) {
            $select.select2('open');
        }

        if (!character || wasOpen) {
            return;
        }

        window.setTimeout(function () {
            var $search = instance.dropdown && instance.dropdown.$search
                ? instance.dropdown.$search
                : instance.$container.find('.select2-search__field');

            if (!$search || !$search.length) {
                return;
            }

            $search.trigger('focus');
            $search.val(character).trigger('input');
        }, 0);
    }

    // Focus the select2 widget when tabbing to the underlying select.
    $(document).on('focus', 'select.select2', function () {
        var $select = $(this);
        window.setTimeout(function () {
            var instance = $select.data('select2');
            if (instance && instance.$selection) {
                instance.$selection.trigger('focus');
            }
        }, 0);
    });

    // Open list and filter when user types without clicking the mouse.
    $(document).on('keydown', '.select2-container .select2-selection', function (e) {
        if (e.ctrlKey || e.metaKey || e.altKey) {
            return;
        }

        if (e.key === 'Tab' || e.key === 'Escape' || e.key === 'Enter' || e.key.indexOf('Arrow') === 0) {
            return;
        }

        if (e.key.length !== 1) {
            return;
        }

        var $container = $(this).closest('.select2-container');
        if ($container.hasClass('select2-container--open')) {
            return;
        }

        var $select = $container.prev('select');
        if (!$select.length || $select.prop('disabled')) {
            return;
        }

        e.preventDefault();
        openAndTypeSearch($select, e.key);
    });

    $(function () {
        window.initPaSelect2($('.select2'));
    });
})(jQuery);

// Date pickers
$(function () {
    if (typeof flatpickr !== 'undefined') {
        flatpickr('.datepicker', { dateFormat: 'd/m/Y', allowInput: true });
    }
});
