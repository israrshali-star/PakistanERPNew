(function () {
    'use strict';

    function showMessage(type, text) {
        $('#je-action-message')
            .removeClass('d-none alert-success alert-danger alert-info')
            .addClass('alert-' + type)
            .text(text);
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function runAction(method, urlSuffix, confirmText, onSuccess) {
        var entryId = $('#je-detail').data('id');
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

        if ($detail.data('can-post') === true) {
            $('#btn-post-entry').on('click', function () {
                if (!confirm('Post this journal entry to the general ledger?')) {
                    return;
                }
                runAction('POST', '/post', null, null);
            });
        }

        if ($detail.data('can-delete') === true) {
            $('#btn-delete-entry').on('click', function () {
                if (!confirm('Delete this draft journal entry? It cannot be undone.')) {
                    return;
                }
                runAction('DELETE', '', null, function () {
                    window.location.href = '/JournalEntries';
                });
            });
        }
    });
})();
