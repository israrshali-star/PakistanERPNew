(function () {
    'use strict';

    var BASE = {
        q: '\u0642', w: '\u0648', e: '\u0639', r: '\u0631', t: '\u062A',
        y: '\u06D2', u: '\u0621', i: '\u06CC', o: '\u06C1', p: '\u067E',
        a: '\u0627', s: '\u0633', d: '\u062F', f: '\u0641', g: '\u06AF',
        h: '\u062D', j: '\u062C', k: '\u06A9', l: '\u0644',
        z: '\u0632', x: '\u0634', c: '\u0686', v: '\u0637', b: '\u0628',
        n: '\u0646', m: '\u0645',
        '1': '\u06F1', '2': '\u06F2', '3': '\u06F3', '4': '\u06F4', '5': '\u06F5',
        '6': '\u06F6', '7': '\u06F7', '8': '\u06F8', '9': '\u06F9', '0': '\u06F0',
        ';': '\u061B', ',': '\u060C', '.': '\u06D4', '/': '/',
        '-': '-', '=': '=',
        '[': ']', ']': '[',
        '`': '\u064D', "'": "'"
    };

    var SHIFT = {
        Q: '\u0652', W: '\u0651', E: '\u0670', R: '\u0691', T: '\u0679',
        Y: '\u064E', U: '\u0626', I: '\u0650', O: '\u06C3', P: '\u064F',
        A: '\u0622', S: '\u0635', D: '\u0688', F: '', G: '\u063A',
        H: '\u06BE', J: '\u0636', K: '\u062E', L: '\u0644',
        Z: '\u0630', X: '\u0698', C: '\u062B', V: '\u0638', B: '\u0628',
        N: '\u06BA', M: '\u0658',
        '!': '1', '@': '2', '#': '3', $: '4', '%': '5',
        '^': '6', '&': '7', '*': '8', '(': '9', ')': '0',
        ':': ':', '"': '"', '?': '\u061F',
        _: '_', '+': '+',
        '{': '}', '}': '{',
        '~': '\u064B'
    };

    function isUrduField(el) {
        return el && el.matches && el.matches('input[lang="ur"], textarea[lang="ur"]');
    }

    function phoneticEnabled(el) {
        return el.getAttribute('data-phonetic') !== 'off';
    }

    function insertAtCursor(input, text) {
        if (!text) {
            return;
        }

        var start = input.selectionStart;
        var end = input.selectionEnd;
        if (start == null || end == null) {
            input.value += text;
            return;
        }

        var next = input.value.slice(0, start) + text + input.value.slice(end);
        var max = parseInt(input.getAttribute('maxlength'), 10);
        if (max > 0 && next.length > max) {
            return;
        }

        input.value = next;
        var pos = start + text.length;
        input.setSelectionRange(pos, pos);
        input.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function mapKey(event) {
        var key = event.key;
        if (!key || key.length !== 1) {
            return undefined;
        }

        if (Object.prototype.hasOwnProperty.call(SHIFT, key)) {
            return SHIFT[key];
        }
        if (Object.prototype.hasOwnProperty.call(BASE, key)) {
            return BASE[key];
        }
        return undefined;
    }

    function onKeyDown(event) {
        if (event.ctrlKey || event.metaKey || event.altKey) {
            return;
        }
        if (event.isComposing) {
            return;
        }
        if (!phoneticEnabled(event.currentTarget)) {
            return;
        }

        var mapped = mapKey(event);
        if (mapped === undefined) {
            return;
        }

        event.preventDefault();
        insertAtCursor(event.currentTarget, mapped);
    }

    function setEnabled(input, enabled) {
        input.setAttribute('data-phonetic', enabled ? 'on' : 'off');
        var wrap = input.parentElement && input.parentElement.querySelector
            ? input.parentElement.querySelector('.urdu-phonetic-toggle')
            : null;
        if (wrap) {
            wrap.classList.toggle('btn-primary', enabled);
            wrap.classList.toggle('btn-outline-secondary', !enabled);
            wrap.setAttribute('aria-pressed', enabled ? 'true' : 'false');
            wrap.title = enabled
                ? 'Phonetic Urdu keyboard is on (click to type English)'
                : 'Phonetic Urdu keyboard is off (click to type Urdu keys)';
        }
        var hint = input.parentElement && input.parentElement.querySelector('.urdu-phonetic-hint');
        if (hint) {
            hint.classList.toggle('d-none', !enabled);
        }
    }

    function enhance(input) {
        if (!input || input.dataset.phoneticBound === '1') {
            return;
        }
        input.dataset.phoneticBound = '1';
        input.addEventListener('keydown', onKeyDown);

        var host = input.parentElement;
        if (!host) {
            return;
        }

        if (!host.querySelector('.urdu-phonetic-toggle')) {
            var toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'btn btn-sm btn-primary urdu-phonetic-toggle';
            toggle.setAttribute('aria-pressed', 'true');
            toggle.innerHTML = '<i class="fa-solid fa-keyboard"></i> Phonetic';
            toggle.addEventListener('click', function () {
                setEnabled(input, !phoneticEnabled(input));
            });

            var bar = document.createElement('div');
            bar.className = 'd-flex justify-content-end mb-1 urdu-phonetic-bar';
            bar.appendChild(toggle);
            host.insertBefore(bar, input);
        }

        if (!host.querySelector('.urdu-phonetic-hint')) {
            var hint = document.createElement('div');
            hint.className = 'form-text urdu-phonetic-hint';
            hint.textContent = 'Phonetic keyboard: type English keys — h=ح  s=س  a=ا  Shift+a=آ  k=ک  Shift+k=خ';
            input.insertAdjacentElement('afterend', hint);
        }

        setEnabled(input, true);
    }

    function enhanceAll(root) {
        (root || document).querySelectorAll('input[lang="ur"], textarea[lang="ur"]').forEach(enhance);
    }

    document.addEventListener('focusin', function (event) {
        if (isUrduField(event.target)) {
            enhance(event.target);
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { enhanceAll(); });
    } else {
        enhanceAll();
    }
})();
