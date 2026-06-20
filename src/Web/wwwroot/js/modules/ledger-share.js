(function (window) {
    'use strict';

    var shareModal = null;
    var currentConfig = null;
    var currentShareInfo = null;

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
        if (!document.getElementById('ledgerShareModal')) {
            return null;
        }
        shareModal = new bootstrap.Modal(document.getElementById('ledgerShareModal'));
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
        $('#ledger-share-error, #ledger-share-success').addClass('d-none');
        if (type === 'success') {
            $('#ledger-share-success').removeClass('d-none').text(message);
        } else if (message) {
            $('#ledger-share-error').removeClass('d-none').text(message);
        }
    }

    function apiBase(config) {
        return config.partyType === 'vendor' ? '/api/vendors/' : '/api/customers/';
    }

    function buildQueryParams(config) {
        var params = {};
        if (config.fromDate) {
            params.from = config.fromDate;
        }
        if (config.toDate) {
            params.to = config.toDate;
        }
        return params;
    }

    function populateModal(info, config) {
        currentShareInfo = info;
        currentConfig = config;

        $('#ledgerShareModalLabel').text(
            config.partyType === 'vendor' ? 'Share Vendor Ledger' : 'Share Customer Ledger'
        );
        $('#ledger-share-summary').text(
            info.partyName + ' · ' + info.partyCode + ' · ' +
            (info.periodLabel || 'Ledger') + ' · ' + formatCurrency(info.closingBalance)
        );
        $('#ledger-share-email').val(info.partyEmail || '');
        $('#ledger-share-whatsapp').val(info.partyMobile || info.partyPhone || '');
        $('#ledger-share-message').val('');

        if (!info.emailConfigured) {
            $('#ledger-share-email-hint').text('SMTP is not configured in appsettings.json.');
            $('#btn-ledger-share-email').prop('disabled', true);
        } else {
            $('#ledger-share-email-hint').text('Sends ledger PDF as email attachment.');
            $('#btn-ledger-share-email').prop('disabled', false);
        }

        showShareAlert(null, null);
    }

    function fetchLedgerPdfBlob(config) {
        var url = apiBase(config) + config.partyId + '/ledger-pdf';
        var query = new URLSearchParams(buildQueryParams(config)).toString();
        if (query) {
            url += '?' + query;
        }

        return fetch(url).then(function (response) {
            if (!response.ok) {
                return response.json().then(function (body) {
                    throw new Error(body.message || body.Message || 'Could not load PDF.');
                });
            }

            var disposition = response.headers.get('Content-Disposition') || '';
            var match = /filename="?([^";]+)"?/i.exec(disposition);
            var fileName = match ? match[1] : 'ledger.pdf';
            return response.blob().then(function (blob) {
                return { blob: blob, fileName: fileName };
            });
        });
    }

    function shareViaWhatsApp() {
        if (!currentShareInfo || !currentConfig) {
            return;
        }

        var phone = normalizeWhatsAppPhone($('#ledger-share-whatsapp').val());
        var message = $('#ledger-share-message').val() || currentShareInfo.whatsAppMessage || '';

        fetchLedgerPdfBlob(currentConfig)
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

    function sendEmail() {
        if (!currentShareInfo || !currentConfig) {
            return;
        }

        var email = $('#ledger-share-email').val();
        if (!email || !email.trim()) {
            showShareAlert('danger', 'Enter recipient email address.');
            return;
        }

        var $btn = $('#btn-ledger-share-email');
        $btn.prop('disabled', true);
        showShareAlert(null, null);

        $.ajax({
            url: apiBase(currentConfig) + currentConfig.partyId + '/ledger-email',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                toEmail: email.trim(),
                message: $('#ledger-share-message').val() || null,
                fromDate: currentConfig.fromDate || null,
                toDate: currentConfig.toDate || null
            })
        })
            .done(function (result) {
                showShareAlert('success', result.message || 'Ledger emailed successfully.');
            })
            .fail(function (xhr) {
                showShareAlert('danger', getApiErrorMessage(xhr, 'Failed to send email.'));
            })
            .always(function () {
                if (currentShareInfo && currentShareInfo.emailConfigured) {
                    $btn.prop('disabled', false);
                }
            });
    }

    function downloadPdf() {
        if (!currentConfig) {
            return;
        }
        var url = apiBase(currentConfig) + currentConfig.partyId + '/ledger-pdf';
        var query = new URLSearchParams(buildQueryParams(currentConfig)).toString();
        if (query) {
            url += '?' + query;
        }
        window.open(url, '_blank');
    }

    function openShareModal(config) {
        var modal = ensureModal();
        if (!modal) {
            alert('Share dialog is not available on this page.');
            return;
        }

        $('#ledger-share-summary').text('Loading...');
        showShareAlert(null, null);

        $.getJSON(apiBase(config) + config.partyId + '/ledger-share-info', buildQueryParams(config))
            .done(function (info) {
                populateModal(info, config);
                modal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load share details.'));
            });
    }

    $(function () {
        $('#btn-ledger-share-email').on('click', sendEmail);
        $('#btn-ledger-share-whatsapp').on('click', shareViaWhatsApp);
        $('#btn-ledger-share-download-pdf').on('click', downloadPdf);
    });

    window.LedgerShare = {
        open: openShareModal
    };
})(window);
