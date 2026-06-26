(function () {
    'use strict';

    function showMessage(type, text) {
        $('#je-action-message')
            .removeClass('d-none alert-success alert-danger alert-info')
            .addClass('alert-' + type)
            .text(text);
    }

    function isDataTrue($el, name) {
        return $el.attr('data-' + name) === 'true';
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function runAction(method, urlSuffix, confirmText, onSuccess) {
        var $detail = $('#je-detail');
        if (!$detail.length) {
            $detail = $('#journal-entry-edit');
        }
        var entryId = $detail.data('id') || $detail.data('entryId');
        $.ajax({
            url: '/api/journal-entries/' + entryId + urlSuffix,
            method: method
        })
            .done(function (result) {
                if (onSuccess) {
                    onSuccess(result);
                    return;
                }
                showMessage('success', result.message || 'Action completed.');
                setTimeout(function () {
                    window.location.reload();
                }, 800);
            })
            .fail(function (xhr) {
                showMessage('danger', getApiErrorMessage(xhr, 'Action failed.'));
            });
    }

    $(function () {
        var $detail = $('#je-detail');
        if (!$detail.length) {
            $detail = $('#journal-entry-edit');
        }
        if (!$detail.length) {
            return;
        }

        if (isDataTrue($detail, 'can-post')) {
            $('#btn-post-entry').on('click', function () {
                if (!confirm('Post this journal entry to the general ledger?')) {
                    return;
                }
                runAction('POST', '/post', null, null);
            });
        }

        if (isDataTrue($detail, 'can-delete')) {
            $('#btn-delete-entry').on('click', function () {
                var status = String($detail.attr('data-status') || '');
                var confirmText = status === 'Posted'
                    ? 'Delete this posted journal entry? It will be removed from the general ledger.'
                    : 'Delete this draft journal entry? It cannot be undone.';
                if (!confirm(confirmText)) {
                    return;
                }
                runAction('DELETE', '', null, function () {
                    window.location.href = '/JournalEntries';
                });
            });
        }
    });
})();

