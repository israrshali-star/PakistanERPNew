(function () {
    'use strict';

    function statusClass(status) {
        if (status === 'Healthy') {
            return 'text-success';
        }
        if (status === 'Degraded') {
            return 'text-warning';
        }
        return 'text-danger';
    }

    function statusIcon(status) {
        if (status === 'Healthy') {
            return 'fa-circle-check';
        }
        if (status === 'Degraded') {
            return 'fa-triangle-exclamation';
        }
        return 'fa-circle-xmark';
    }

    function renderChecks(checks) {
        var $list = $('#health-checks-list');
        $list.empty();

        if (!checks || !checks.length) {
            $list.append('<div class="list-group-item px-0 text-muted">No health checks reported.</div>');
            return;
        }

        checks.forEach(function (check) {
            var tags = (check.tags || []).join(', ');
            var error = check.error ? '<div class="text-danger small">' + $('<div>').text(check.error).html() + '</div>' : '';
            $list.append(
                '<div class="list-group-item d-flex flex-wrap justify-content-between align-items-start gap-2 px-0">' +
                '<div>' +
                '<div class="fw-semibold"><i class="fa-solid ' + statusIcon(check.status) + ' me-1 ' + statusClass(check.status) + '"></i>' +
                $('<div>').text(check.name).html() + '</div>' +
                '<div class="text-muted small">' + $('<div>').text(check.description || '—').html() + '</div>' +
                error +
                (tags ? '<div class="text-muted small">Tags: ' + $('<div>').text(tags).html() + '</div>' : '') +
                '</div>' +
                '<div class="text-end">' +
                '<span class="badge ' + (check.status === 'Healthy' ? 'bg-success' : 'bg-danger') + '">' + check.status + '</span>' +
                '<div class="text-muted small mt-1">' + (check.duration || 0).toFixed(0) + ' ms</div>' +
                '</div>' +
                '</div>'
            );
        });
    }

    function loadHealth() {
        $('#health-overall-status').text('Checking…').removeClass('text-success text-warning text-danger');
        $('#health-checked-at').text('');

        $.getJSON('/api/system-health')
            .done(function (data) {
                var status = data.status || 'Unknown';
                $('#health-overall-status')
                    .text(status)
                    .addClass(statusClass(status));
                $('#health-checked-at').text('Checked at ' + new Date().toLocaleString());
                renderChecks(data.checks || []);
            })
            .fail(function (xhr) {
                $('#health-overall-status')
                    .text('Unavailable')
                    .addClass('text-danger');
                $('#health-checked-at').text('Health API request failed.');
                $('#health-checks-list').html(
                    '<div class="list-group-item px-0 text-danger">Could not load health status.</div>'
                );
            });
    }

    $(function () {
        loadHealth();
        $('#btn-refresh-health').on('click', loadHealth);
    });
})();
