/* Mangrove CMS: table editor inside the page editor. Plain JS, no dependencies.
   State lives in `cols` and `rows`; the grid is re-rendered from it and written to the
   hidden #table-json field, which is saved together with the page. */
(function () {
    'use strict';
    var root = document.getElementById('table-editor');
    var store = document.getElementById('table-json');
    if (!root || !store) return;
    var form = store.form;
    var TYPES = window.MANGROVE_TYPES || [];

    var T = { Text: 0, Serial: 1, Number: 2, Phone: 3, Email: 4, Image: 5, Link: 6, Date: 7 };
    var EDIT_PAGE = 100;   // rows shown at once in the editor
    var initial = {};
    try { initial = JSON.parse(store.value || '{}'); } catch (e) { initial = {}; }
    var cols = (initial.columns || []).map(function (c) { return { key: String(c.key), name: c.name, type: c.type, width: c.width || null }; });
    var rows = (initial.rows || []).map(function (r) { return { cells: Object.assign({}, r.cells) }; });
    var newKey = 1, page = 1, filter = '', dirty = false;

    var $ = function (sel) { return root.querySelector(sel); };
    var head = $('[data-head]'), body = $('[data-body]'), colgroup = $('[data-cols]');
    var countEl = $('[data-count]'), pagerEl = $('[data-pager]');
    var hint = document.querySelector('[data-save-hint]');

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function token() { var el = document.querySelector('input[name="__RequestVerificationToken"]'); return el ? el.value : ''; }

    function changed() {
        dirty = true;
        if (hint) hint.hidden = false;
        sync();
    }
    function sync() {
        store.value = JSON.stringify({
            columns: cols.map(function (c) { return { key: c.key, name: c.name, type: c.type, width: c.width || null }; }),
            rows: rows.map(function (r) { return { cells: r.cells }; }),
        });
    }

    // ---------- Rendering ----------
    function visibleRows() {
        var list = rows.map(function (r, i) { return { r: r, i: i }; });
        if (!filter) return list;
        var f = filter.toLowerCase();
        return list.filter(function (x) {
            return Object.keys(x.r.cells).some(function (k) { return String(x.r.cells[k]).toLowerCase().indexOf(f) >= 0; });
        });
    }
    function defaultWidth(type) { return { 1: 90, 3: 160, 4: 200, 5: 140, 7: 150 }[type] || 180; }

    function render() {
        var hasCols = cols.length > 0;
        $('[data-empty]').hidden = hasCols;
        $('[data-scroll]').hidden = !hasCols;
        $('[data-foot]').hidden = !hasCols;
        $('.te-bar').hidden = !hasCols; // the empty state has its own buttons

        colgroup.innerHTML = '<col style="width:56px">' + cols.map(function (c) {
            return '<col style="width:' + (c.width || defaultWidth(c.type)) + 'px">';
        }).join('') + '<col style="width:44px">';

        head.innerHTML = '<tr><th class="te-corner" scope="col"><span class="sr-only">Row</span></th>' + cols.map(function (c, ci) {
            var opts = TYPES.map(function (t) {
                return '<option value="' + t.value + '"' + (t.value === c.type ? ' selected' : '') + '>' + esc(t.label) + '</option>';
            }).join('');
            var title = (TYPES.filter(function (t) { return t.value === c.type; })[0] || {}).title || '';
            return '<th scope="col" data-ci="' + ci + '">' +
                '<div class="te-colrow">' +
                '<input class="te-colname" value="' + esc(c.name) + '" aria-label="Column name" data-colname="' + ci + '">' +
                '<span class="te-coltools">' +
                '<button type="button" class="te-iconbtn" title="Move left" data-colmove="' + ci + '" data-dir="-1"' + (ci === 0 ? ' disabled' : '') + '>&#8249;</button>' +
                '<button type="button" class="te-iconbtn" title="Move right" data-colmove="' + ci + '" data-dir="1"' + (ci === cols.length - 1 ? ' disabled' : '') + '>&#8250;</button>' +
                '<button type="button" class="te-iconbtn danger" title="Delete column" data-coldel="' + ci + '">&times;</button>' +
                '</span></div>' +
                '<select class="te-coltype" aria-label="Column type" title="' + esc(title) + '" data-coltype="' + ci + '">' + opts + '</select>' +
                '<span class="te-resize" data-resize="' + ci + '" title="Drag to resize. Double-click for automatic width."></span></th>';
        }).join('') + '<th class="te-corner" scope="col"><span class="sr-only">Actions</span></th></tr>';

        var list = visibleRows();
        var pages = Math.max(1, Math.ceil(list.length / EDIT_PAGE));
        if (page > pages) page = pages;
        var slice = list.slice((page - 1) * EDIT_PAGE, page * EDIT_PAGE);

        body.innerHTML = slice.map(function (x) {
            return '<tr data-ri="' + x.i + '">' +
                '<td class="te-handle"><span class="te-grip" data-grip title="Drag to move"><svg class="ic"><use href="#i-grip"/></svg></span>' +
                '<span class="te-rownum">' + (x.i + 1) + '</span></td>' +
                cols.map(function (c) { return '<td>' + cellHtml(c, x.r, x.i) + '</td>'; }).join('') +
                '<td class="te-rowact"><button type="button" class="te-iconbtn danger" title="Delete row" data-rowdel="' + x.i + '">&times;</button></td></tr>';
        }).join('') || '<tr><td class="te-none" colspan="' + (cols.length + 2) + '">' +
            (filter ? 'No rows match “' + esc(filter) + '”.' : 'No rows yet. Click Add row to start.') + '</td></tr>';

        countEl.textContent = hasCols ? rows.length + (rows.length === 1 ? ' row' : ' rows') + (filter ? ', ' + list.length + ' shown' : '') : '';
        pagerEl.innerHTML = pages > 1
            ? '<button type="button" class="btn sm" data-page="' + (page - 1) + '"' + (page === 1 ? ' disabled' : '') + '>Previous</button>' +
              '<span>Rows ' + ((page - 1) * EDIT_PAGE + 1) + '–' + Math.min(page * EDIT_PAGE, list.length) + ' of ' + list.length + '</span>' +
              '<button type="button" class="btn sm" data-page="' + (page + 1) + '"' + (page === pages ? ' disabled' : '') + '>Next</button>'
            : '';
    }

    function cellHtml(c, r, ri) {
        var v = r.cells[c.key] || '';
        var attrs = ' data-cell="' + ri + '" data-key="' + esc(c.key) + '"';
        switch (c.type) {
            case T.Serial: return '<span class="te-serial">' + (ri + 1) + '</span>';
            case T.Image:
                return '<div class="te-imgcell">' +
                    (v ? '<img src="' + esc(v) + '" alt="">' : '<span class="te-noimg">No image</span>') +
                    '<button type="button" class="linklike" data-pickimg="' + ri + '" data-key="' + esc(c.key) + '">' + (v ? 'Change' : 'Choose') + '</button>' +
                    (v ? '<button type="button" class="linklike danger" data-clearimg="' + ri + '" data-key="' + esc(c.key) + '">Remove</button>' : '') +
                    '</div>';
            case T.Date: return '<input type="date" class="te-input" value="' + esc(v) + '"' + attrs + '>';
            case T.Number: return '<input class="te-input te-num" inputmode="decimal" value="' + esc(v) + '"' + attrs + '>';
            case T.Phone: return '<input type="tel" class="te-input" value="' + esc(v) + '"' + attrs + '>';
            case T.Email: return '<input type="email" class="te-input" value="' + esc(v) + '"' + attrs + '>';
            case T.Link: return '<input type="url" class="te-input" placeholder="https://" value="' + esc(v) + '"' + attrs + '>';
            default: return '<input class="te-input" value="' + esc(v) + '"' + attrs + '>';
        }
    }

    // ---------- Editing ----------
    root.addEventListener('input', function (e) {
        var t = e.target;
        if (t.dataset.cell !== undefined) { rows[+t.dataset.cell].cells[t.dataset.key] = t.value; changed(); }
        else if (t.dataset.colname !== undefined) { cols[+t.dataset.colname].name = t.value; changed(); }
        else if (t.matches('[data-filter]')) { filter = t.value.trim(); page = 1; render(); }
        e.stopPropagation(); // the page form's own change tracking isn't needed for the grid
    });
    root.addEventListener('change', function (e) {
        var t = e.target;
        if (t.dataset.coltype !== undefined) { cols[+t.dataset.coltype].type = +t.value; changed(); render(); }
    });

    // Enter moves down a row (like a spreadsheet) and never submits the page form.
    root.addEventListener('keydown', function (e) {
        var t = e.target;
        if (e.key !== 'Enter' || t.tagName !== 'INPUT') return;
        e.preventDefault();
        if (t.dataset.cell === undefined) return;
        var td = t.closest('td'), tr = td.parentElement;
        var idx = [].indexOf.call(tr.children, td);
        var next = tr.nextElementSibling && tr.nextElementSibling.children[idx];
        var input = next && next.querySelector('input');
        if (input) input.focus(); else { addRow(); focusLastRow(idx); }
    });

    function addRow() {
        rows.push({ cells: {} });
        filter = '';
        $('[data-filter]').value = '';
        page = Math.ceil(rows.length / EDIT_PAGE);
        changed(); render();
    }
    function focusLastRow(idx) {
        var last = body.lastElementChild;
        var input = last && (idx != null && last.children[idx] ? last.children[idx].querySelector('input') : last.querySelector('input'));
        if (input) input.focus();
    }
    function addColumn(name, type) {
        cols.push({ key: 'n' + (newKey++), name: name || 'Column ' + (cols.length + 1), type: type || T.Text, width: null });
        changed();
    }

    root.addEventListener('click', function (e) {
        var b = e.target.closest('button');
        if (!b) return;
        var act = b.dataset.act;
        if (act === 'add-row') {
            addRow(); focusLastRow();
        } else if (act === 'add-col') {
            addColumn(); render();
            var inputs = head.querySelectorAll('.te-colname');
            if (inputs.length) { inputs[inputs.length - 1].focus(); inputs[inputs.length - 1].select(); }
        } else if (act === 'preset-officers') {
            addColumn('SN', T.Serial); addColumn('Name', T.Text); addColumn('Rank', T.Text);
            addColumn('Image', T.Image); addColumn('Phone number', T.Phone);
            if (!rows.length) rows.push({ cells: {} });
            render(); focusLastRow();
        } else if (act === 'import') {
            openImport();
        } else if (b.dataset.coldel !== undefined) {
            var ci = +b.dataset.coldel, col = cols[ci];
            var used = rows.some(function (r) { return r.cells[col.key]; });
            if (used && !confirm('Delete the column “' + col.name + '” and everything in it?')) return;
            rows.forEach(function (r) { delete r.cells[col.key]; });
            cols.splice(ci, 1);
            changed(); render();
        } else if (b.dataset.colmove !== undefined) {
            var i = +b.dataset.colmove, j = i + (+b.dataset.dir);
            if (j < 0 || j >= cols.length) return;
            var tmp = cols[i]; cols[i] = cols[j]; cols[j] = tmp;
            changed(); render();
        } else if (b.dataset.rowdel !== undefined) {
            var ri = +b.dataset.rowdel;
            var hasData = Object.keys(rows[ri].cells).some(function (k) { return rows[ri].cells[k]; });
            if (hasData && !confirm('Delete row ' + (ri + 1) + '?')) return;
            rows.splice(ri, 1);
            changed(); render();
        } else if (b.dataset.pickimg !== undefined) {
            var r = +b.dataset.pickimg, key = b.dataset.key;
            window.MangroveMedia.open(true, function (m) { rows[r].cells[key] = m.url; changed(); render(); });
        } else if (b.dataset.clearimg !== undefined) {
            delete rows[+b.dataset.clearimg].cells[b.dataset.key];
            changed(); render();
        } else if (b.dataset.page !== undefined) {
            page = +b.dataset.page; render();
            $('[data-scroll]').scrollTop = 0;
        }
    });

    // ---------- Column resizing ----------
    root.addEventListener('pointerdown', function (e) {
        var h = e.target.closest('[data-resize]');
        if (!h) return;
        e.preventDefault();
        var ci = +h.dataset.resize, colEl = colgroup.children[ci + 1];
        var startX = e.clientX, startW = colEl.getBoundingClientRect().width || cols[ci].width || 160;
        h.setPointerCapture(e.pointerId);
        root.classList.add('te-resizing');
        function move(ev) {
            var w = Math.max(90, Math.min(1000, Math.round(startW + ev.clientX - startX)));
            colEl.style.width = w + 'px';
            cols[ci].width = w;
        }
        function up() {
            h.removeEventListener('pointermove', move);
            h.removeEventListener('pointerup', up);
            root.classList.remove('te-resizing');
            changed();
        }
        h.addEventListener('pointermove', move);
        h.addEventListener('pointerup', up);
    });
    root.addEventListener('dblclick', function (e) {
        var h = e.target.closest('[data-resize]');
        if (!h) return;
        cols[+h.dataset.resize].width = null;
        changed(); render();
    });

    // ---------- Row reordering ----------
    var dragFrom = null;
    body.addEventListener('pointerdown', function (e) {
        var g = e.target.closest('[data-grip]');
        if (g) g.closest('tr').draggable = true;
    });
    body.addEventListener('dragstart', function (e) {
        var tr = e.target.closest('tr[data-ri]');
        if (!tr) return;
        dragFrom = +tr.dataset.ri;
        tr.classList.add('te-dragging');
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', String(dragFrom));
    });
    body.addEventListener('dragover', function (e) {
        var tr = e.target.closest('tr[data-ri]');
        if (dragFrom === null || !tr) return;
        e.preventDefault();
        body.querySelectorAll('.te-over').forEach(function (x) { x.classList.remove('te-over'); });
        tr.classList.add('te-over');
    });
    body.addEventListener('drop', function (e) {
        var tr = e.target.closest('tr[data-ri]');
        if (dragFrom === null || !tr) return;
        e.preventDefault();
        var to = +tr.dataset.ri;
        if (to !== dragFrom) { rows.splice(to, 0, rows.splice(dragFrom, 1)[0]); changed(); }
        dragFrom = null; render();
    });
    body.addEventListener('dragend', function () { dragFrom = null; render(); });

    // ---------- Excel import (read on the server, applied here; saved with the page) ----------
    var dlg = document.getElementById('import-dialog');
    var fileInput = document.getElementById('import-file');
    var errEl = dlg.querySelector('[data-import-error]');
    function openImport() {
        errEl.textContent = '';
        fileInput.value = '';
        dlg.querySelector('[data-import-mode]').hidden = rows.length === 0 && cols.length === 0;
        dlg.showModal();
    }
    dlg.addEventListener('click', function (e) { if (e.target.closest('[data-close]')) dlg.close(); });
    dlg.querySelector('[data-import-go]').addEventListener('click', function () {
        var btn = this;
        if (!fileInput.files.length) { errEl.textContent = 'Choose a spreadsheet first.'; return; }
        var fd = new FormData();
        fd.append('file', fileInput.files[0]);
        fd.append('__RequestVerificationToken', token());
        btn.disabled = true; errEl.textContent = ''; btn.textContent = 'Reading…';
        fetch('/admin/pagetable/parse', { method: 'POST', body: fd, credentials: 'same-origin' })
            .then(function (res) { return res.json().then(function (j) { return { ok: res.ok, j: j }; }); })
            .then(function (x) {
                btn.disabled = false; btn.textContent = 'Import';
                if (!x.ok) { errEl.textContent = x.j.error || 'The file couldn\'t be read.'; return; }
                var mode = (dlg.querySelector('input[name="import-mode"]:checked') || {}).value;
                applyImport(x.j, mode === 'append');
                dlg.close();
            })
            .catch(function () { btn.disabled = false; btn.textContent = 'Import'; errEl.textContent = 'Upload failed. Check your connection.'; });
    });

    function applyImport(sheet, append) {
        if (!append) { cols = []; rows = []; }
        var map = sheet.headers.map(function (h, i) {
            var existing = cols.filter(function (c) { return c.name.trim().toLowerCase() === h.trim().toLowerCase(); })[0];
            if (existing) return existing;
            var c = { key: 'n' + (newKey++), name: h, type: sheet.types[i], width: null };
            cols.push(c);
            return c;
        });
        sheet.rows.forEach(function (values) {
            var cells = {};
            map.forEach(function (c, i) { if (c.type !== T.Serial && values[i]) cells[c.key] = values[i]; });
            rows.push({ cells: cells });
        });
        filter = ''; page = 1;
        changed(); render();
    }

    // ---------- Saving with the page ----------
    form.addEventListener('submit', function (e) {
        if (cols.some(function (c) { return !c.name.trim(); })) {
            e.preventDefault();
            alert('Every column needs a name.');
            return;
        }
        sync();
        dirty = false;
    });
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); form.requestSubmit(); }
    });
    window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

    sync();
    render();
})();
