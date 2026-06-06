(function () {

    'use strict';



    var accountModal = null;

    var accountTypes = [];


    var canCreate = false;

    var canEdit = false;

    var canDelete = false;

    var editContext = {

        hasChildren: false,

        isGroupAccount: false

    };



    function escapeHtml(text) {

        return $('<div>').text(text ?? '').html();

    }



    function showFormError(message) {

        $('#account-form-error').removeClass('d-none').text(message);

    }



    function clearFormError() {

        $('#account-form-error').addClass('d-none').text('');

    }



    function countAccounts(accounts) {

        if (!accounts || accounts.length === 0) {

            return 0;

        }



        return accounts.reduce(function (sum, acc) {

            return sum + 1 + countAccounts(acc.children);

        }, 0);

    }



    function loadLookups() {

        return $.getJSON('/api/lookup/account-types')

            .then(function (types) {

                accountTypes = types;

                populateTypeSelect();

            });

    }



    function populateTypeSelect(selectedTypeId) {

        var $type = $('#account-type');

        $type.empty().append('<option value="">Select type...</option>');

        accountTypes.forEach(function (t) {

            $type.append(

                $('<option></option>').val(t.id).text(t.code + ' — ' + t.name)

            );

        });

        if (selectedTypeId) {

            $type.val(selectedTypeId);

        }

        return loadSubAccountTypes($type.val(), $('#account-subtype').val());

    }



    function loadSubAccountTypes(typeId, selectedSubTypeId) {

        var $sub = $('#account-subtype');



        if (!typeId) {

            $sub.empty().append('<option value="">Select account type first...</option>');

            $sub.prop('disabled', true);

            return loadParentAccounts();

        }



        $sub.empty().append('<option value="">Select sub type...</option>');



        $sub.prop('disabled', true);



        return $.getJSON('/api/lookup/sub-account-types', { typeId: parseInt(typeId, 10) })

            .done(function (items) {

                items.forEach(function (s) {

                    $sub.append(

                        $('<option></option>').val(s.id).text(s.code + ' — ' + s.name)

                    );

                });



                if (selectedSubTypeId) {

                    $sub.val(String(selectedSubTypeId));

                }



                $sub.prop('disabled', false);

                loadParentAccounts();

            })

            .fail(function () {

                $sub.prop('disabled', false);

                showFormError('Could not load sub-account types for the selected account type.');

            });

    }



    function loadParentAccounts(selectedParentId) {

        var typeId = parseInt($('#account-type').val(), 10);

        var subTypeId = parseInt($('#account-subtype').val(), 10);

        var excludeId = $('#account-id').val();

        var $parent = $('#account-parent');



        $parent.empty().append('<option value="">None — Main / Header account</option>');



        if (!typeId || !subTypeId) {

            updateOpeningBalanceField();

            return $.Deferred().resolve().promise();

        }



        var params = { typeId: typeId, subTypeId: subTypeId };

        if (excludeId) {

            params.excludeAccountId = parseInt(excludeId, 10);

        }



        return $.getJSON('/api/chart-of-accounts/parents', params)

            .done(function (parents) {

                parents.forEach(function (p) {

                    $parent.append(

                        $('<option></option>')

                            .val(p.id)

                            .text(p.accountNumber + ' — ' + p.accountName)

                    );

                });



                if (selectedParentId) {

                    $parent.val(String(selectedParentId));

                }



                updateOpeningBalanceField();

            })

            .fail(function () {

                updateOpeningBalanceField();

            });

    }



    function updateOpeningBalanceField() {

        var isGroupHeader = editContext.hasChildren || editContext.isGroupAccount;

        var $opening = $('#opening-balance');

        var $hint = $('#opening-balance-hint');



        if (isGroupHeader) {

            $opening.prop('readonly', true).prop('disabled', false).addClass('bg-light');

            $hint.removeClass('d-none').text('Total opening balance is the sum of all child accounts.');

        } else {

            $opening.prop('readonly', false).removeAttr('readonly').prop('disabled', false).removeClass('bg-light');

            $hint.addClass('d-none');

        }

    }



    function appendAccountRows($tbody, accounts, depth) {

        accounts.forEach(function (acc) {

            var indent = depth * 20;

            var isGroup = acc.isGroupAccount;

            var statusBadge = acc.isActive

                ? '<span class="badge bg-success">Active</span>'

                : '<span class="badge bg-secondary">Inactive</span>';



            var nameHtml = escapeHtml(acc.accountName);

            if (isGroup) {

                nameHtml = '<i class="fa-solid fa-folder-tree me-1 text-primary"></i>' +

                    '<strong>' + nameHtml + '</strong>';

            } else if (depth > 0) {

                nameHtml = '<span class="text-muted me-1">↳</span>' + nameHtml;

            }



            var isMainHeader = !acc.parentAccountId;

            var actions = '';

            if (canCreate && isMainHeader) {

                actions += '<button type="button" class="btn btn-link btn-sm p-0 me-2 btn-add-child" ' +

                    'data-id="' + acc.id + '" data-number="' + escapeHtml(acc.accountNumber) + '" ' +

                    'data-name="' + escapeHtml(acc.accountName) + '" title="Add child account">' +

                    '<i class="fa-solid fa-plus"></i></button>';

            }

            if (canEdit) {

                actions += '<button type="button" class="btn btn-link btn-sm p-0 me-2 btn-edit-account" data-id="' + acc.id + '" title="Edit"><i class="fa-solid fa-pen"></i></button>';

            }

            if (canDelete && !isGroup) {

                actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-account" data-id="' + acc.id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';

            }



            var openingClass = isGroup ? ' fw-bold text-primary' : '';

            var balanceClass = isGroup ? ' fw-bold text-primary' : ' fw-semibold';



            $tbody.append(

                '<tr class="' + (isGroup ? 'coa-group-row' : 'coa-child-row') + '" data-coa-id="' + acc.id + '">' +

                '<td style="padding-left:' + (12 + indent) + 'px"><code>' + escapeHtml(acc.accountNumber) + '</code></td>' +

                '<td>' + nameHtml + '</td>' +

                '<td class="text-end text-currency' + openingClass + '">' + formatCurrency(acc.openingBalance) + '</td>' +

                '<td class="text-end text-currency' + balanceClass + '">' + formatCurrency(acc.runningBalance) + '</td>' +

                '<td>' + statusBadge + '</td>' +

                '<td class="text-end">' + (actions || '<span class="text-muted small">—</span>') + '</td>' +

                '</tr>'

            );



            if (acc.children && acc.children.length > 0) {

                appendAccountRows($tbody, acc.children, depth + 1);

            }

        });

    }



    function renderTree(tree) {

        var $container = $('#coa-tree-container');

        $container.empty();



        if (!tree || tree.length === 0) {

            $('#coa-total-count').addClass('d-none');

            $container.html('<div class="alert alert-info mb-0">No accounts found. Create your first account.</div>');

            return;

        }



        var totalAccounts = tree.reduce(function (sum, typeNode) {

            return sum + typeNode.subTypes.reduce(function (subSum, st) {

                return subSum + countAccounts(st.accounts);

            }, 0);

        }, 0);



        $('#coa-total-count')

            .removeClass('d-none')

            .text(totalAccounts + ' account' + (totalAccounts === 1 ? '' : 's'));



        tree.forEach(function (typeNode) {

            var accountCount = typeNode.subTypes.reduce(function (sum, st) {

                return sum + countAccounts(st.accounts);

            }, 0);



            if (accountCount === 0) {

                return;

            }



            var $card = $(

                '<div class="card mb-3 coa-type-card">' +

                '<div class="card-header py-2">' +

                '<i class="fa-solid fa-folder-tree me-2 text-primary"></i>' +

                '<strong>' + escapeHtml(typeNode.typeCode) + '</strong>' +

                '<span class="ms-2">' + escapeHtml(typeNode.typeName) + '</span>' +

                '<span class="badge bg-primary ms-2">' + accountCount + '</span>' +

                '</div>' +

                '<div class="card-body p-0"></div></div>'

            );



            var $body = $card.find('.card-body');



            typeNode.subTypes.forEach(function (subNode) {

                if (!subNode.accounts || subNode.accounts.length === 0) {

                    return;

                }



                var subCount = countAccounts(subNode.accounts);

                var $subSection = $('<div class="coa-subtype-section"></div>');

                $subSection.append(

                    '<div class="coa-subtype-header">' +

                    escapeHtml(subNode.subTypeName) +

                    '<span class="badge bg-light text-primary ms-2">' + subCount + '</span>' +

                    '</div>'

                );



                var $table = $(

                    '<table class="table table-sm table-hover coa-accounts-table mb-0">' +

                    '<thead><tr>' +

                    '<th>Number</th><th>Name</th>' +

                    '<th class="text-end">Opening</th><th class="text-end">Balance</th>' +

                    '<th>Status</th><th class="text-end">Actions</th>' +

                    '</tr></thead><tbody></tbody></table>'

                );



                appendAccountRows($table.find('tbody'), subNode.accounts, 0);

                $subSection.append($table);

                $body.append($subSection);

            });



            $container.append($card);

        });



        if ($container.children().length === 0) {

            $container.html('<div class="alert alert-info mb-0">No accounts found. Create your first account.</div>');

        }

    }



    function loadTree() {

        $('#coa-tree-container').html(

            '<div class="text-center text-muted py-5"><i class="fa-solid fa-spinner fa-spin me-2"></i>Loading...</div>'

        );



        return $.getJSON('/api/chart-of-accounts/tree')

            .done(renderTree)

            .fail(function (xhr) {

                var message = getErrorMessage(xhr, 'Failed to load chart of accounts.');

                $('#coa-tree-container').html(

                    '<div class="alert alert-danger mb-0">' + escapeHtml(message) + '</div>'

                );

            });

    }



    function resetEditContext() {

        editContext = { hasChildren: false, isGroupAccount: false };

    }



    function openCreateModal(parentPreset) {

        clearFormError();

        resetEditContext();

        $('#accountModalLabel').text(parentPreset ? 'New Child Account' : 'New Account');

        $('#account-id').val('');

        $('#account-form')[0].reset();

        $('#account-active').prop('checked', true);

        $('#opening-balance').val('0');

        populateTypeSelect(parentPreset ? parentPreset.typeId : null);



        if (parentPreset) {

            loadSubAccountTypes(parentPreset.typeId, parentPreset.subTypeId);

            loadParentAccounts(parentPreset.parentAccountId).always(function () {

                suggestNumber();

                accountModal.show();

            });

        } else {

            loadParentAccounts().always(function () {

                accountModal.show();

            });

        }

    }



    function openEditModal(id) {

        clearFormError();

        resetEditContext();



        $.getJSON('/api/chart-of-accounts/' + id)

            .done(function (account) {

                editContext.hasChildren = account.hasChildren;

                editContext.isGroupAccount = account.isGroupAccount;



                $('#accountModalLabel').text('Edit Account');

                $('#account-id').val(account.id);

                populateTypeSelect(account.typeId);

                loadSubAccountTypes(account.typeId, account.subTypeId);



                loadParentAccounts(account.parentAccountId).always(function () {

                    $('#account-number').val(account.accountNumber);

                    $('#account-name').val(account.accountName);

                    $('#opening-balance').val(account.openingBalance);

                    $('#account-description').val(account.description || '');

                    $('#account-active').prop('checked', account.isActive);



                    if (account.hasChildren) {

                        $('#account-parent').prop('disabled', true);

                    } else {

                        $('#account-parent').prop('disabled', false);

                    }



                    updateOpeningBalanceField();

                    accountModal.show();

                });

            })

            .fail(function () {

                alert('Failed to load account details.');

            });

    }



    function openChildModal(parentId) {

        $.getJSON('/api/chart-of-accounts/' + parentId)

            .done(function (parent) {

                openCreateModal({

                    typeId: parent.typeId,

                    subTypeId: parent.subTypeId,

                    parentAccountId: parent.id

                });

            })

            .fail(function () {

                alert('Failed to load parent account.');

            });

    }



    function suggestNumber() {

        var typeId = parseInt($('#account-type').val(), 10);

        var subTypeId = parseInt($('#account-subtype').val(), 10);

        var parentAccountId = $('#account-parent').val();



        if (!typeId || !subTypeId) {

            showFormError('Select account type and sub type first.');

            return;

        }



        var params = { typeId: typeId, subTypeId: subTypeId };

        if (parentAccountId) {

            params.parentAccountId = parseInt(parentAccountId, 10);

        }



        $.getJSON('/api/chart-of-accounts/suggest-number', params)

            .done(function (result) {

                clearFormError();

                $('#account-number').val(result.accountNumber);

            })

            .fail(function () {

                showFormError('Could not suggest an account number.');

            });

    }



    function getErrorMessage(xhr, fallback) {

        if (xhr.responseJSON) {

            if (xhr.responseJSON.message) {

                return xhr.responseJSON.message;

            }

            if (xhr.responseJSON.title) {

                return xhr.responseJSON.title;

            }

        }

        if (xhr.status === 403) {

            return 'You do not have permission to save accounts.';

        }

        return fallback;

    }



    function showExistingAccountNotice(message, accountId) {

        var $notice = $('#coa-notice');

        var html = escapeHtml(message);

        if (accountId) {

            html += ' <button type="button" class="btn btn-link btn-sm p-0 align-baseline" id="btn-view-existing-account">Show in list</button>';

        }

        $notice.removeClass('d-none').html(html);

        if (accountId) {

            $notice.find('#btn-view-existing-account').off('click').on('click', function () {

                highlightAccountRow(accountId);

            });

        }

    }



    function hideExistingAccountNotice() {

        $('#coa-notice').addClass('d-none').empty();

    }



    function highlightAccountRow(accountId) {

        var $row = $('tr[data-coa-id="' + accountId + '"]');

        if ($row.length === 0) {

            return;

        }



        $('.coa-row-highlight').removeClass('coa-row-highlight');

        $row.addClass('coa-row-highlight');

        $row[0].scrollIntoView({ behavior: 'smooth', block: 'center' });

    }



    function checkAccountNumberExists() {

        if ($('#account-id').val()) {

            return;

        }



        var number = $('#account-number').val().trim();

        if (!number) {

            return;

        }



        $.getJSON('/api/chart-of-accounts/by-number', { accountNumber: number })

            .done(function (account) {

                showFormError(

                    'Account ' + account.accountNumber + ' (' + account.accountName + ') already exists.'

                );

                showExistingAccountNotice(

                    'Account ' + account.accountNumber + ' — ' + account.accountName + ' is already in the chart of accounts.',

                    account.id

                );

            });

    }



    function saveAccount(e) {

        e.preventDefault();

        clearFormError();



        var typeId = parseInt($('#account-type').val(), 10);

        var subTypeId = parseInt($('#account-subtype').val(), 10);

        var parentVal = $('#account-parent').val();



        if (!typeId || !subTypeId) {

            showFormError('Please select both account type and sub-account type.');

            return;

        }



        var id = $('#account-id').val();

        var payload = {

            id: id ? parseInt(id, 10) : null,

            accountNumber: $('#account-number').val().trim(),

            accountName: $('#account-name').val().trim(),

            typeId: typeId,

            subTypeId: subTypeId,

            parentAccountId: parentVal ? parseInt(parentVal, 10) : null,

            description: $('#account-description').val().trim() || null,

            openingBalance: parseFloat($('#opening-balance').val()) || 0,

            isActive: $('#account-active').is(':checked')

        };



        if (!payload.accountNumber || !payload.accountName) {

            showFormError('Account number and name are required.');

            return;

        }



        var $saveBtn = $('#btn-save-account');

        $saveBtn.prop('disabled', true);



        var request = id

            ? $.ajax({

                url: '/api/chart-of-accounts/' + id,

                method: 'PUT',

                contentType: 'application/json',

                data: JSON.stringify(payload)

            })

            : $.ajax({

                url: '/api/chart-of-accounts',

                method: 'POST',

                contentType: 'application/json',

                data: JSON.stringify(payload)

            });



        request

            .done(function () {

                accountModal.hide();

                hideExistingAccountNotice();

                loadTree();

            })

            .fail(function (xhr) {

                var message = getErrorMessage(xhr, 'Failed to save account.');

                var existingId = xhr.responseJSON && xhr.responseJSON.existingAccountId;



                if (existingId) {

                    accountModal.hide();

                    showExistingAccountNotice(message, existingId);

                    loadTree().done(function () {

                        highlightAccountRow(existingId);

                    });

                } else {

                    showFormError(message);

                }

            })

            .always(function () {

                $saveBtn.prop('disabled', false);

            });

    }



    function deleteAccount(id) {

        if (!confirm('Delete this account? This cannot be undone.')) {

            return;

        }



        $.ajax({

            url: '/api/chart-of-accounts/' + id,

            method: 'DELETE'

        })

            .done(function () {

                loadTree();

            })

            .fail(function (xhr) {

                var message = 'Failed to delete account.';

                if (xhr.responseJSON && xhr.responseJSON.message) {

                    message = xhr.responseJSON.message;

                }

                alert(message);

            });

    }



    function detectPermissions() {

        var $perms = $('#coa-permissions');

        canCreate = $perms.attr('data-can-create') === 'true';

        canEdit = $perms.attr('data-can-edit') === 'true';

        canDelete = $perms.attr('data-can-delete') === 'true';

    }



    $(function () {

        accountModal = new bootstrap.Modal(document.getElementById('accountModal'));

        detectPermissions();



        loadLookups().always(loadTree);



        $('#btn-add-account').on('click', function () {

            openCreateModal(null);

        });

        $('#btn-suggest-number').on('click', suggestNumber);



        $('#account-type').on('change', function () {

            clearFormError();

            loadSubAccountTypes($(this).val()).always(function () {

                if (!$('#account-id').val() && $('#account-type').val()) {

                    $('#account-number').val('');

                }

            });

        });



        $('#account-subtype').on('change', function () {

            loadParentAccounts().always(function () {

                if (!$('#account-id').val() && $('#account-type').val() && $('#account-subtype').val()) {

                    suggestNumber();

                }

            });

        });



        $('#account-parent').on('change', function () {

            updateOpeningBalanceField();

            if (!$('#account-id').val() && $('#account-type').val() && $('#account-subtype').val()) {

                suggestNumber();

            }

        });



        $('#accountModal').on('hidden.bs.modal', function () {

            $('#account-parent').prop('disabled', false);

            resetEditContext();

            updateOpeningBalanceField();

        });



        $('#account-form').on('submit', saveAccount);

        $('#account-number').on('blur', checkAccountNumberExists);



        $('#coa-tree-container').on('click', '.btn-edit-account', function () {

            openEditModal($(this).data('id'));

        });



        $('#coa-tree-container').on('click', '.btn-delete-account', function () {

            deleteAccount($(this).data('id'));

        });



        $('#coa-tree-container').on('click', '.btn-add-child', function () {

            openChildModal($(this).data('id'));

        });



        $('#btn-export-coa').on('click', function () {

            window.location.href = '/api/chart-of-accounts/export';

        });

    });

})();


