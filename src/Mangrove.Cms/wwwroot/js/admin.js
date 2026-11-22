/* Mangrove CMS — admin behaviour. Plain JS, no dependencies. */
(function () {
    'use strict';

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    // ---- Confirm dialogs: <form data-confirm="Are you sure?"> ----
    document.addEventListener('submit', function (e) {
        // e.target is the form being submitted, even when the button sits outside it (form="...").
        // A button can carry its own question (data-confirm-click), e.g. Delete inside a Save form.
        var msg = (e.submitter && e.submitter.getAttribute('data-confirm-click')) || e.target.getAttribute('data-confirm');
        if (msg && !window.confirm(msg)) e.preventDefault();
    }, true);

    // ---- Markdown editor ----
    var editor = document.querySelector('.md-editor');
    if (editor) {
        var ta = editor.querySelector('.md-input');
        var preview = editor.querySelector('.md-preview');

        function wrap(before, after, placeholder) {
            var s = ta.selectionStart, e = ta.selectionEnd;
            var sel = ta.value.slice(s, e) || placeholder || '';
            ta.setRangeText(before + sel + after, s, e, 'end');
            if (!ta.value.slice(s, e).length) {
                ta.selectionStart = s + before.length;
                ta.selectionEnd = s + before.length + sel.length;
            }
            ta.focus();
        }

        function prefixLines(prefix, numbered) {
            var s = ta.value.lastIndexOf('\n', ta.selectionStart - 1) + 1;
            var e = ta.selectionEnd;
            var lines = ta.value.slice(s, e).split('\n');
            var out = lines.map(function (l, i) { return (numbered ? (i + 1) + '. ' : prefix) + l; }).join('\n');
            ta.setRangeText(out, s, e, 'end');
            ta.focus();
        }

        var actions = {
            bold: function () { wrap('**', '**', 'bold text'); },
            italic: function () { wrap('*', '*', 'italic text'); },
            h2: function () { prefixLines('## '); },
            h3: function () { prefixLines('### '); },
            quote: function () { prefixLines('> '); },
            ul: function () { prefixLines('- '); },
            ol: function () { prefixLines('', true); },
            code: function () {
                var sel = ta.value.slice(ta.selectionStart, ta.selectionEnd);
                if (sel.indexOf('\n') >= 0) wrap('```\n', '\n```'); else wrap('`', '`', 'code');
            },
            link: function () {
                var url = window.prompt('Link URL', 'https://');
                if (url) wrap('[', '](' + url + ')', 'link text');
            },
            image: function () {
                openPicker(false, function (m) {
                    var md = m.isImage ? '![' + (m.altText || m.fileName) + '](' + m.url + ')' : '[' + m.fileName + '](' + m.url + ')';
                    ta.setRangeText(md, ta.selectionStart, ta.selectionEnd, 'end');
                    ta.focus();
                });
            }
        };

        editor.querySelector('.md-toolbar').addEventListener('click', function (e) {
            var b = e.target.closest('button');
            if (!b) return;
            if (b.dataset.md && actions[b.dataset.md]) actions[b.dataset.md]();
            if (b.dataset.tab) showTab(b.dataset.tab);
        });

        ta.addEventListener('keydown', function (e) {
            if (!(e.ctrlKey || e.metaKey)) return;
            var k = e.key.toLowerCase();
            if (k === 'b') { e.preventDefault(); actions.bold(); }
            else if (k === 'i') { e.preventDefault(); actions.italic(); }
            else if (k === 'k') { e.preventDefault(); actions.link(); }
            else if (k === 's') { e.preventDefault(); editor.closest('form').requestSubmit(); }
        });

        function showTab(name) {
            editor.querySelectorAll('.tab').forEach(function (t) { t.classList.toggle('active', t.dataset.tab === name); });
            var isPreview = name === 'preview';
            ta.hidden = isPreview;
            preview.hidden = !isPreview;
            if (isPreview) {
                preview.innerHTML = '<p class="muted">Rendering…</p>';
                var body = new FormData();
                body.append('content', ta.value);
                body.append('__RequestVerificationToken', token());
                fetch(editor.dataset.previewUrl, { method: 'POST', body: body, credentials: 'same-origin' })
                    .then(function (r) { return r.text(); })
                    .then(function (html) { preview.innerHTML = html || '<p class="muted">Nothing to preview.</p>'; })
                    .catch(function () { preview.innerHTML = '<p class="danger">Preview failed.</p>'; });
            }
        }

        // Paste or drop images straight into the editor → upload and insert.
        function uploadAndInsert(files) {
            uploadFiles(files, function (res) {
                res.files.forEach(function (m) {
                    var md = (m.isImage ? '![' + m.fileName + '](' : '[' + m.fileName + '](') + m.url + ')\n';
                    ta.setRangeText(md, ta.selectionStart, ta.selectionEnd, 'end');
                });
                if (res.errors.length) alert(res.errors.join('\n'));
            });
        }
        ta.addEventListener('paste', function (e) {
            var files = Array.prototype.filter.call((e.clipboardData || {}).files || [], function (f) { return f.type.indexOf('image/') === 0; });
            if (files.length) { e.preventDefault(); uploadAndInsert(files); }
        });
        ta.addEventListener('dragover', function (e) { if (e.dataTransfer.types.indexOf('Files') >= 0) e.preventDefault(); });
        ta.addEventListener('drop', function (e) {
            if (e.dataTransfer.files.length) { e.preventDefault(); uploadAndInsert(e.dataTransfer.files); }
        });

        // Warn before leaving with unsaved changes.
        var form = editor.closest('form');
        var dirty = false;
        form.addEventListener('input', function () { dirty = true; });
        form.addEventListener('submit', function () { dirty = false; });
        window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });
    }

    // ---- Media upload helper ----
    function uploadFiles(files, done) {
        var body = new FormData();
        Array.prototype.forEach.call(files, function (f) { body.append('files', f); });
        body.append('__RequestVerificationToken', token());
        fetch('/admin/media/upload?json=true', { method: 'POST', body: body, credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(done)
            .catch(function (err) { alert('Upload failed (' + err.message + ').'); });
    }

    // ---- Media picker dialog ----
    var picker = document.getElementById('media-picker');
    var pickCallback = null, pickImagesOnly = false;

    function loadPicker(q) {
        var grid = picker.querySelector('[data-picker-grid]');
        var status = picker.querySelector('[data-picker-status]');
        status.textContent = 'Loading…';
        fetch('/admin/media/list?imagesOnly=' + pickImagesOnly + '&q=' + encodeURIComponent(q || ''), { credentials: 'same-origin' })
            .then(function (r) { return r.json(); })
            .then(function (items) {
                status.textContent = items.length ? 'Click a file to use it.' : 'No files yet — upload one.';
                grid.innerHTML = items.map(function (m, i) {
                    var thumb = m.isImage ? '<img src="' + esc(m.url) + '" alt="" loading="lazy">'
                                          : '<span class="file-icon">' + esc(m.fileName.split('.').pop().toUpperCase()) + '</span>';
                    return '<button type="button" class="btn" data-i="' + i + '" title="' + esc(m.fileName) + '">' + thumb + '</button>';
                }).join('');
                grid.onclick = function (e) {
                    var b = e.target.closest('button[data-i]');
                    if (!b) return;
                    picker.close();
                    if (pickCallback) pickCallback(items[+b.dataset.i]);
                };
            });
    }

    function openPicker(imagesOnly, cb) {
        if (!picker) return;
        pickCallback = cb;
        pickImagesOnly = imagesOnly;
        picker.querySelector('[data-picker-search]').value = '';
        picker.showModal();
        loadPicker('');
    }

    if (picker) {
        var searchTimer;
        picker.querySelector('[data-picker-search]').addEventListener('input', function (e) {
            clearTimeout(searchTimer);
            var v = e.target.value;
            searchTimer = setTimeout(function () { loadPicker(v); }, 250);
        });
        picker.querySelector('[data-picker-upload]').addEventListener('change', function (e) {
            uploadFiles(e.target.files, function (res) {
                if (res.errors.length) alert(res.errors.join('\n'));
                e.target.value = '';
                loadPicker('');
            });
        });
    }

    // Shared with other admin scripts (table editor, notice editor).
    window.MangroveMedia = { open: openPicker, upload: uploadFiles };

    // ---- Featured image ----
    var pickFeatured = document.querySelector('[data-pick-featured]');
    if (pickFeatured) {
        var idInput = document.getElementById('featured-id');
        var previewBox = document.getElementById('featured-preview');
        var clear = document.querySelector('[data-clear-featured]');
        pickFeatured.addEventListener('click', function () {
            openPicker(true, function (m) {
                idInput.value = m.id;
                previewBox.innerHTML = '<img src="' + esc(m.url) + '" alt="">';
                clear.hidden = false;
                idInput.dispatchEvent(new Event('input', { bubbles: true }));
            });
        });
        clear.addEventListener('click', function () {
            idInput.value = '';
            previewBox.innerHTML = '';
            clear.hidden = true;
            idInput.dispatchEvent(new Event('input', { bubbles: true }));
        });
    }

    // ---- Media library drag & drop ----
    var zone = document.querySelector('[data-dropzone]');
    if (zone) {
        ['dragenter', 'dragover'].forEach(function (ev) {
            zone.addEventListener(ev, function (e) { e.preventDefault(); zone.classList.add('over'); });
        });
        ['dragleave', 'drop'].forEach(function (ev) {
            zone.addEventListener(ev, function () { zone.classList.remove('over'); });
        });
        zone.addEventListener('drop', function (e) {
            e.preventDefault();
            uploadFiles(e.dataTransfer.files, function () { location.reload(); });
        });
    }
})();
