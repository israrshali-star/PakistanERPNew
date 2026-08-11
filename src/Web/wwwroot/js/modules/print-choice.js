(function (window) {
    'use strict';

    var modal = null;
    var currentOptions = null;

    function ensureModal() {
        if (modal) {
            return modal;
        }
        if (!document.getElementById('printChoiceModal')) {
            return null;
        }
        modal = new bootstrap.Modal(document.getElementById('printChoiceModal'));
        return modal;
    }

    function printPdfUrl(url) {
        fetch(url)
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('Could not load PDF for printing.');
                }
                return response.blob();
            })
            .then(function (blob) {
                var objectUrl = window.URL.createObjectURL(blob);
                var printWindow = window.open(objectUrl, '_blank');
                if (!printWindow) {
                    window.URL.revokeObjectURL(objectUrl);
                    alert('Allow pop-ups to print this document.');
                    return;
                }
                var printed = false;
                var triggerPrint = function () {
                    if (printed) {
                        return;
                    }
                    printed = true;
                    try {
                        printWindow.focus();
                        printWindow.print();
                    } catch (e) {
                        // Browser may block; user can still print from the opened tab.
                    }
                    setTimeout(function () {
                        window.URL.revokeObjectURL(objectUrl);
                    }, 60000);
                };
                printWindow.addEventListener('load', triggerPrint);
                setTimeout(triggerPrint, 800);
            })
            .catch(function (err) {
                alert(err.message || 'Could not print PDF.');
            });
    }

    function open(options) {
        currentOptions = options || {};
        var instance = ensureModal();
        if (!instance) {
            if (typeof currentOptions.onPrint === 'function') {
                currentOptions.onPrint();
            }
            return;
        }

        $('#printChoiceModalLabel').text(currentOptions.title || 'Print options');
        $('#print-choice-summary').text(
            currentOptions.summary || 'Choose Print to open the printer dialog, or Save as PDF to download/open a PDF file.'
        );
        instance.show();
    }

    $(function () {
        $('#btn-print-choice-print').on('click', function () {
            if (modal) {
                modal.hide();
            }
            if (!currentOptions) {
                return;
            }
            if (typeof currentOptions.onPrint === 'function') {
                currentOptions.onPrint();
            } else if (currentOptions.pdfUrl) {
                printPdfUrl(currentOptions.pdfUrl);
            }
        });

        $('#btn-print-choice-pdf').on('click', function () {
            if (modal) {
                modal.hide();
            }
            if (!currentOptions) {
                return;
            }
            if (typeof currentOptions.onPdf === 'function') {
                currentOptions.onPdf();
            } else if (currentOptions.pdfUrl) {
                window.open(currentOptions.pdfUrl, '_blank');
            }
        });
    });

    window.PrintChoice = {
        open: open,
        printPdfUrl: printPdfUrl
    };
})(window);
