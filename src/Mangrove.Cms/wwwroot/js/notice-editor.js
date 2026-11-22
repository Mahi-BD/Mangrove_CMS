/* Mangrove CMS: notice list inside the page editor. Plain JS, no dependencies.
   Notices are kept in the hidden #notices-json field and saved together with the page.
   Files upload to the media library straight away, so only their ids travel with the page. */
(function () {
    'use strict';
    var root = document.getElementById('notice-editor');
    var store = document.getElementById('notices-json');
    if (!root || !store) return;
    var form = store.form;
    var hint = document.querySelector('[data-save-hint]');

    var notices = [];
    try { notices = JSON.parse(store.value || '[]'); } catch (e) { notices = []; }
    var filter = '', dirty = false, editing = null, draftFiles = [];

    var listEl = root.querySelector('[data-list]'), emptyEl = root.querySelector('[data-empty]');
    var countEl = root.querySelector('[data-count]');
    var dlg = document.getElementById('notice-dialog');
    var f = function (name) { return dlg.querySelector('[data-f="' + name + '"]'); };

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function today() { var d = new Date(); return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'); }
    function niceDate(iso) {
        var p = (iso || '').split('-');
        if (p.length !== 3) return iso || '';
        var m = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][+p[1] - 1];
        return (+p[2]) + ' ' + m + ' ' + p[0];
    }
    function size(b) { return !b ? '' : b >= 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(b / 1024)) + ' KB'; }
    function ext(name) { var m = /\.([a-z0-9]+)$/i.exec(name || ''); return m ? m[1].toUpperCase() : 'FILE'; }

    function sortNotices() {
        notices.sort(function (a, b) {
            if (a.pinned !== b.pinned) return a.pinned ? -1 : 1;
            if (a.date !== b.date) return a.date < b.date ? 1 : -1;
            return (b.id || 1e9) - (a.id || 1e9);
        });
    }
    function sync() {
        store.value = JSON.stringify(notices.map(function (n) {
            return {
                id: n.id || 0, title: n.title, date: n.date, description: n.description || '',
                pinned: !!n.pinned, published: n.published !== false,
                files: n.files.map(function (x) { return { mediaId: x.mediaId, fileName: x.fileName, size: x.size || 0, url: x.url }; }),
            };
        }));
    }
    function changed() { dirty = true; if (hint) hint.hidden = false; sync(); }

    function render() {
        var f2 = filter.toLowerCase();
        var shown = notices.map(function (n, i) { return { n: n, i: i }; })
            .filter(function (x) { return !f2 || x.n.title.toLowerCase().indexOf(f2) >= 0; });
        countEl.textContent = notices.length ? '(' + notices.length + ')' : '';
        emptyEl.hidden = notices.length > 0;
        listEl.innerHTML = shown.map(function (x) {
            var n = x.n;
            return '<li class="ne-item' + (n.published === false ? ' is-hidden' : '') + '">' +
                '<time class="ne-date">' + esc(niceDate(n.date)) + '</time>' +
                '<div class="ne-main"><button type="button" class="ne-title linklike" data-edit="' + x.i + '">' + esc(n.title) + '</button>' +
                (n.pinned ? ' <span class="flag">Pinned</span>' : '') +
                (n.published === false ? ' <span class="flag">Hidden</span>' : '') +
                (n.id ? '' : ' <span class="flag">New</span>') +
                '<div class="ne-files">' + (n.files.length
                    ? n.files.map(function (fl) { return '<span class="ne-file"><b>' + esc(ext(fl.fileName)) + '</b>' + esc(fl.fileName) + '</span>'; }).join('')
                    : '<span class="muted small">No files</span>') + '</div></div>' +
                '<div class="ne-actions"><button type="button" class="btn sm" data-edit="' + x.i + '">Edit</button>' +
                '<button type="button" class="btn sm quiet danger" data-del="' + x.i + '">Delete</button></div></li>';
        }).join('') || (notices.length ? '<li class="ne-none muted">No notices match “' + esc(filter) + '”.</li>' : '');
    }

    // ---------- Dialog ----------
    function drawFiles() {
        dlg.querySelector('[data-files]').innerHTML = draftFiles.map(function (fl, i) {
            return '<li><span class="file-name"><b class="ne-ext">' + esc(ext(fl.fileName)) + '</b> ' + esc(fl.fileName) + '</span>' +
                '<span class="small muted">' + size(fl.size) + '</span>' +
                '<button type="button" class="linklike danger small" data-unfile="' + i + '">Remove</button></li>';
        }).join('');
    }
    function openDialog(i) {
        editing = i;
        var n = i == null ? { title: '', date: today(), description: '', pinned: false, published: true, files: [] } : notices[i];
        dlg.querySelector('[data-dialog-title]').textContent = i == null ? 'Add notice' : 'Edit notice';
        dlg.querySelector('[data-dialog-ok]').textContent = i == null ? 'Add notice' : 'Done';
        dlg.querySelector('[data-title-error]').textContent = '';
        dlg.querySelector('[data-upload-state]').textContent = '';
        f('title').value = n.title; f('date').value = n.date; f('description').value = n.description || '';
        f('pinned').checked = !!n.pinned; f('published').checked = n.published !== false;
        draftFiles = n.files.slice();
        drawFiles();
        dlg.showModal();
        f('title').focus();
    }
    function addFile(m) {
        if (draftFiles.some(function (x) { return x.mediaId === m.id; })) return;
        draftFiles.push({ mediaId: m.id, fileName: m.fileName, size: m.size || 0, url: m.url });
        drawFiles();
    }

    dlg.addEventListener('click', function (e) {
        var b = e.target.closest('button');
        if (!b) return;
        if (b.matches('[data-close]')) { dlg.close(); }
        else if (b.dataset.unfile !== undefined) { draftFiles.splice(+b.dataset.unfile, 1); drawFiles(); }
        else if (b.matches('[data-pick-library]')) { window.MangroveMedia.open(false, addFile); }
        else if (b.matches('[data-dialog-ok]')) {
            var title = f('title').value.trim();
            if (!title) { dlg.querySelector('[data-title-error]').textContent = 'Give the notice a title.'; f('title').focus(); return; }
            var n = {
                id: editing == null ? 0 : notices[editing].id,
                title: title, date: f('date').value || today(), description: f('description').value,
                pinned: f('pinned').checked, published: f('published').checked, files: draftFiles.slice(),
            };
            if (editing == null) notices.push(n); else notices[editing] = n;
            sortNotices(); changed(); render();
            dlg.close();
        }
    });
    // Enter in a dialog field must not submit the page form.
    dlg.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && e.target.tagName === 'INPUT') { e.preventDefault(); dlg.querySelector('[data-dialog-ok]').click(); }
    });
    dlg.addEventListener('input', function (e) { e.stopPropagation(); });

    var upload = dlg.querySelector('[data-upload]'), drop = dlg.querySelector('[data-drop]');
    var stateEl = dlg.querySelector('[data-upload-state]');
    function uploadFiles(files) {
        if (!files.length) return;
        stateEl.textContent = 'Uploading ' + files.length + (files.length === 1 ? ' file…' : ' files…');
        var sizes = {};
        [].forEach.call(files, function (fl) { sizes[fl.name] = fl.size; });
        window.MangroveMedia.upload(files, function (res) {
            res.files.forEach(function (m) { m.size = sizes[m.fileName]; addFile(m); });
            stateEl.textContent = res.errors.length ? res.errors.join(' ') : '';
            upload.value = '';
        });
    }
    upload.addEventListener('change', function () { uploadFiles(upload.files); });
    ['dragenter', 'dragover'].forEach(function (ev) { drop.addEventListener(ev, function (e) { e.preventDefault(); drop.classList.add('over'); }); });
    ['dragleave', 'drop'].forEach(function (ev) { drop.addEventListener(ev, function () { drop.classList.remove('over'); }); });
    drop.addEventListener('drop', function (e) { e.preventDefault(); uploadFiles(e.dataTransfer.files); });

    // ---------- List actions ----------
    root.addEventListener('click', function (e) {
        var b = e.target.closest('button');
        if (!b) return;
        if (b.dataset.act === 'add') openDialog(null);
        else if (b.dataset.edit !== undefined) openDialog(+b.dataset.edit);
        else if (b.dataset.del !== undefined) {
            var n = notices[+b.dataset.del];
            if (!confirm('Delete the notice “' + n.title + '”? Its files stay in the media library.')) return;
            notices.splice(+b.dataset.del, 1);
            changed(); render();
        }
    });
    root.querySelector('[data-filter]').addEventListener('input', function (e) { filter = e.target.value.trim(); render(); e.stopPropagation(); });
    root.querySelector('[data-filter]').addEventListener('keydown', function (e) { if (e.key === 'Enter') e.preventDefault(); });

    form.addEventListener('submit', function () { sync(); dirty = false; });
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's' && !dlg.open) { e.preventDefault(); form.requestSubmit(); }
    });
    window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

    sortNotices(); sync(); render();
})();
