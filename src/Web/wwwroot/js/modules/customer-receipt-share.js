(function (window) {
    'use strict';

    var shareModal = null;
    var currentShareInfo = null;
    var currentReceiptId = null;

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function ensureModal() {
        if (shareModal) {
            return shareModal;
        }
        if (!document.getElementById('receiptShareModal')) {
            return null;
        }
        shareModal = new bootstrap.Modal(document.getElementById('receiptShareModal'));
        return shareModal;
    }

    function formatCurrency(value) {
        var num = parseFloat(value) || 0;
        return 'PKR ' + num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function normalizeWhatsAppPhone(phone) {
        var digits = String(phone || '').replace(/\D/g, '');
        if (!digits) {
            return '';
        }
        if (digits.startsWith('0')) {
            digits = '92' + digits.substring(1);
        }
        if (digits.length === 10 && digits.startsWith('3')) {
            digits = '92' + digits;
        }
        return digits;
    }

    function showShareAlert(type, message) {
        $('#receipt-share-error, #receipt-share-success').addClass('d-none');
        if (type === 'success') {
            $('#receipt-share-success').removeClass('d-none').text(message);
        } else if (message) {
            $('#receipt-share-error').removeClass('d-none').text(message);
        }
    }

    function populateModal(info) {
        currentShareInfo = info;
        currentReceiptId = info.receiptId;

        $('#receipt-share-summary').text(
            info.receiptNumber + ' · ' + info.customerName + ' · ' +
            formatCurrency(info.amount) + ' · ' + info.paymentMethodLabel
        );
        $('#receipt-share-whatsapp').val(info.customerMobile || info.customerPhone || '');
        $('#receipt-share-message').val(info.whatsAppMessage || '');
        showShareAlert(null, null);
    }

    function fetchReceiptPdfBlob(receiptId) {
        return fetch('/api/customer-receipts/' + receiptId + '/pdf').then(function (response) {
            if (!response.ok) {
                return response.json().then(function (body) {
                    throw new Error(body.message || body.Message || 'Could not load PDF.');
                });
            }

            var disposition = response.headers.get('Content-Disposition') || '';
            var match = /filename="?([^";]+)"?/i.exec(disposition);
            var fileName = match ? match[1] : 'receipt.pdf';
            return response.blob().then(function (blob) {
                return { blob: blob, fileName: fileName };
            });
        });
    }

    function shareViaWhatsApp() {
        if (!currentShareInfo || !currentReceiptId) {
            return;
        }

        var phone = normalizeWhatsAppPhone($('#receipt-share-whatsapp').val());
        var message = ($('#receipt-share-message').val() || currentShareInfo.whatsAppMessage || '').trim();
        if (!message) {
            showShareAlert('danger', 'Enter a message to send.');
            return;
        }

        fetchReceiptPdfBlob(currentReceiptId)
            .then(function (pdf) {
                var file = new File([pdf.blob], pdf.fileName, { type: 'application/pdf' });
                if (navigator.canShare && navigator.canShare({ files: [file] })) {
                    return navigator.share({
                        files: [file],
                        title: pdf.fileName,
                        text: message
                    });
                }

                var link = document.createElement('a');
                link.href = window.URL.createObjectURL(pdf.blob);
                link.download = pdf.fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();

                var waUrl = phone
                    ? 'https://wa.me/' + phone + '?text=' + encodeURIComponent(message)
                    : 'https://wa.me/?text=' + encodeURIComponent(message);
                window.open(waUrl, '_blank');
                showShareAlert('success', 'PDF downloaded. Attach it in WhatsApp if not shared automatically.');
            })
            .catch(function (err) {
                showShareAlert('danger', err.message || 'Could not prepare WhatsApp share.');
            });
    }

    function downloadPdf() {
        if (!currentReceiptId) {
            return;
        }
        window.open('/api/customer-receipts/' + currentReceiptId + '/pdf', '_blank');
    }

    function openShareModal(receiptId) {
        var modal = ensureModal();
        if (!modal) {
            alert('Share dialog is not available on this page.');
            return;
        }

        $('#receipt-share-summary').text('Loading...');
        showShareAlert(null, null);

        $.getJSON('/api/customer-receipts/' + receiptId + '/share-info')
            .done(function (info) {
                populateModal(info);
                modal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load receipt share details.'));
            });
    }

    $(function () {
        $('#btn-receipt-share-whatsapp').on('click', shareViaWhatsApp);
        $('#btn-receipt-share-download-pdf').on('click', downloadPdf);
    });

    window.ReceiptShare = {
        open: openShareModal
    };
})(window);
