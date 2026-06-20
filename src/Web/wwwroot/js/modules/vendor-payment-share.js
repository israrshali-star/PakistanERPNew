(function (window) {
    'use strict';

    var shareModal = null;
    var currentShareInfo = null;
    var currentPaymentId = null;

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
        if (!document.getElementById('vendorPaymentShareModal')) {
            return null;
        }
        shareModal = new bootstrap.Modal(document.getElementById('vendorPaymentShareModal'));
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
        $('#vendor-payment-share-error, #vendor-payment-share-success').addClass('d-none');
        if (type === 'success') {
            $('#vendor-payment-share-success').removeClass('d-none').text(message);
        } else if (message) {
            $('#vendor-payment-share-error').removeClass('d-none').text(message);
        }
    }

    function populateModal(info) {
        currentShareInfo = info;
        currentPaymentId = info.paymentId;

        $('#vendor-payment-share-summary').text(
            info.paymentNumber + ' · ' + info.vendorName + ' · ' +
            formatCurrency(info.amount) + ' · ' + info.paymentMethodLabel
        );
        $('#vendor-payment-share-whatsapp').val(info.vendorPhone || '');
        $('#vendor-payment-share-message').val(info.whatsAppMessage || '');
        showShareAlert(null, null);
    }

    function fetchPaymentPdfBlob(paymentId) {
        return fetch('/api/vendor-payments/' + paymentId + '/pdf').then(function (response) {
            if (!response.ok) {
                return response.json().then(function (body) {
                    throw new Error(body.message || body.Message || 'Could not load PDF.');
                });
            }

            var disposition = response.headers.get('Content-Disposition') || '';
            var match = /filename="?([^";]+)"?/i.exec(disposition);
            var fileName = match ? match[1] : 'payment.pdf';
            return response.blob().then(function (blob) {
                return { blob: blob, fileName: fileName };
            });
        });
    }

    function shareViaWhatsApp() {
        if (!currentShareInfo || !currentPaymentId) {
            return;
        }

        var phone = normalizeWhatsAppPhone($('#vendor-payment-share-whatsapp').val());
        var message = ($('#vendor-payment-share-message').val() || currentShareInfo.whatsAppMessage || '').trim();
        if (!message) {
            showShareAlert('danger', 'Enter a message to send.');
            return;
        }

        fetchPaymentPdfBlob(currentPaymentId)
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
        if (!currentPaymentId) {
            return;
        }
        window.open('/api/vendor-payments/' + currentPaymentId + '/pdf', '_blank');
    }

    function openShareModal(paymentId) {
        var modal = ensureModal();
        if (!modal) {
            alert('Share dialog is not available on this page.');
            return;
        }

        $('#vendor-payment-share-summary').text('Loading...');
        showShareAlert(null, null);

        $.getJSON('/api/vendor-payments/' + paymentId + '/share-info')
            .done(function (info) {
                populateModal(info);
                modal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load payment share details.'));
            });
    }

    $(function () {
        $('#btn-vendor-payment-share-whatsapp').on('click', shareViaWhatsApp);
        $('#btn-vendor-payment-share-download-pdf').on('click', downloadPdf);
    });

    window.VendorPaymentShare = {
        open: openShareModal
    };
})(window);
