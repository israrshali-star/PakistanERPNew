// Global ERP helpers — amount only; pair with .text-currency for PKR prefix
window.formatCurrency = function (value) {
    const num = parseFloat(value) || 0;
    return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
};

// Initialize Select2 on elements with .select2 class
$(function () {
    if ($.fn.select2) {
        $('.select2').select2({ theme: 'bootstrap-5', width: '100%' });
    }

    if (typeof flatpickr !== 'undefined') {
        flatpickr('.datepicker', { dateFormat: 'd/m/Y', allowInput: true });
    }
});
