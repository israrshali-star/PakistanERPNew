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
