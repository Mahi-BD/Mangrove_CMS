/* Mangrove CMS: drag-and-drop menu builder. Plain JS, no dependencies.
   The menu is a flat list where `depth` says how far each item is nested (0 = top level),
   like WordPress. Dragging moves an item together with its sub-items. */
(function () {
    'use strict';
    var data = window.MANGROVE_MENU;
    var root = document.getElementById('menu-builder');
    if (!data || !root) return;

    var MAX = data.maxDepth;           // deepest allowed depth
    var INDENT = 32;                   // px per level, in the list and when dragging
    var TYPE_LABEL = { custom: 'Custom link', page: 'Page', post: 'Post', category: 'Category', tag: 'Tag' };
    var items = data.items.map(function (i) {
        return { label: i.label, url: i.url, newTab: i.newTab, depth: i.depth, linkType: i.linkType, linkId: i.linkId, original: i.original, open: false };
    });
    var dirty = false;

    var listEl = root.querySelector('[data-list]'), emptyEl = root.querySelector('[data-empty]');
    var stateEl = root.querySelector('[data-state]'), saveBtn = root.querySelector('[data-save]');

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function token() { var el = document.querySelector('input[name="__RequestVerificationToken"]'); return el ? el.value : ''; }
    function setDirty(v) {
        dirty = v;
        stateEl.textContent = v ? 'Unsaved changes' : stateEl.textContent;
        stateEl.classList.toggle('mb-unsaved', v);
    }

    // ---------- Tree helpers on the flat list ----------
    /** Index just past item i's last descendant. */
    function blockEnd(i) {
        var j = i + 1;
        while (j < items.length && items[j].depth > items[i].depth) j++;
        return j;
    }
    function parentIndex(i) {
        for (var j = i - 1; j >= 0; j--) if (items[j].depth < items[i].depth) return j;
        return -1;
    }
    /** Re-clamp depths so each item is at most one level below the one before it. */
    function normalize() {
        var prev = -1;
        items.forEach(function (it) {
            it.depth = Math.max(0, Math.min(it.depth, prev + 1, MAX));
            prev = it.depth;
        });
    }

    // ---------- Rendering ----------
    function render() {
        emptyEl.hidden = items.length > 0;
        listEl.innerHTML = items.map(function (it, i) {
            var parent = parentIndex(i);
            var prevSibling = (function () {
                for (var j = i - 1; j >= 0; j--) { if (items[j].depth < it.depth) return -1; if (items[j].depth === it.depth) return j; }
                return -1;
            })();
            var isCustom = it.linkType === 'custom';
            var sub = it.depth > 0 ? '<span class="mb-sub">sub-item of ' + esc(items[parent] ? items[parent].label : '') + '</span>' : '';
            return '<li class="mb-item' + (it.open ? ' open' : '') + (it.original === 'Deleted' ? ' broken' : '') + '" data-i="' + i + '" style="--d:' + it.depth + '">' +
                '<div class="mb-bar">' +
                '<span class="mb-handle" data-handle title="Drag to move" aria-hidden="true"><svg class="ic"><use href="#i-grip"/></svg></span>' +
                '<button type="button" class="mb-label" data-toggle="' + i + '" aria-expanded="' + it.open + '">' + esc(it.label) + sub + '</button>' +
                '<span class="mb-type">' + TYPE_LABEL[it.linkType] + '</span>' +
                '<button type="button" class="mb-chev" data-toggle="' + i + '" aria-label="Edit ' + esc(it.label) + '"><svg class="ic"><use href="#i-down"/></svg></button>' +
                '</div>' +
                (it.open ? '<div class="mb-detail">' +
                    '<div class="mb-fields">' +
                    '<div><label for="mb-l' + i + '">Label</label><input id="mb-l' + i + '" value="' + esc(it.label) + '" data-field="label" data-i="' + i + '"></div>' +
                    '<div><label for="mb-u' + i + '">Link</label><input id="mb-u' + i + '" value="' + esc(it.url) + '" data-field="url" data-i="' + i + '"' + (isCustom ? '' : ' readonly') + '></div>' +
                    '</div>' +
                    (isCustom ? '' : '<p class="small muted">' + (it.original === 'Deleted'
                        ? 'The ' + TYPE_LABEL[it.linkType].toLowerCase() + ' this pointed to was deleted, so visitors won\'t see this item.'
                        : 'Links to the ' + TYPE_LABEL[it.linkType].toLowerCase() + ' “' + esc(it.original) + '”. The link updates if its address changes.') + '</p>') +
                    '<label class="check"><input type="checkbox" data-field="newTab" data-i="' + i + '"' + (it.newTab ? ' checked' : '') + '> Open in a new tab</label>' +
                    '<div class="mb-moves">' +
                    '<span class="small muted">Move</span>' +
                    (i > 0 ? '<button type="button" class="linklike small" data-move="up" data-i="' + i + '">Up one</button>' : '') +
                    (blockEnd(i) < items.length ? '<button type="button" class="linklike small" data-move="down" data-i="' + i + '">Down one</button>' : '') +
                    (prevSibling >= 0 && it.depth < MAX ? '<button type="button" class="linklike small" data-move="in" data-i="' + i + '">Under ' + esc(items[prevSibling].label) + '</button>' : '') +
                    (it.depth > 0 ? '<button type="button" class="linklike small" data-move="out" data-i="' + i + '">Out from under ' + esc(items[parent].label) + '</button>' : '') +
                    (i > 0 ? '<button type="button" class="linklike small" data-move="top" data-i="' + i + '">To the top</button>' : '') +
                    '<span class="spacer"></span>' +
                    '<button type="button" class="linklike small danger" data-remove="' + i + '">Remove</button>' +
                    '</div></div>' : '') +
                '</li>';
        }).join('');
    }

    // ---------- Editing ----------
    listEl.addEventListener('input', function (e) {
        var t = e.target, i = +t.dataset.i;
        if (!t.dataset.field) return;
        if (t.type === 'checkbox') items[i][t.dataset.field] = t.checked;
        else items[i][t.dataset.field] = t.value;
        if (t.dataset.field === 'label') {
            var lbl = listEl.querySelector('.mb-item[data-i="' + i + '"] .mb-label');
            if (lbl) lbl.firstChild.textContent = t.value || '(no label)';
        }
        setDirty(true);
    });
    listEl.addEventListener('change', function (e) {
        var t = e.target;
        if (t.type === 'checkbox' && t.dataset.field) { items[+t.dataset.i][t.dataset.field] = t.checked; setDirty(true); }
    });

    function moveBlock(from, to) {
        // Move items[from..blockEnd(from)) so that it starts at index `to` of the remaining list.
        var end = blockEnd(from);
        var block = items.splice(from, end - from);
        items.splice.apply(items, [to, 0].concat(block));
        return block;
    }
    function shiftDepth(block, delta) { block.forEach(function (it) { it.depth += delta; }); }

    listEl.addEventListener('click', function (e) {
        var b = e.target.closest('button');
        if (!b) return;
        if (b.dataset.toggle !== undefined) {
            var it = items[+b.dataset.toggle];
            it.open = !it.open;
            render();
            if (it.open) { var f = listEl.querySelector('#mb-l' + b.dataset.toggle); if (f) f.focus(); }
            return;
        }
        if (b.dataset.remove !== undefined) {
            var i = +b.dataset.remove, end = blockEnd(i), kids = end - i - 1;
            if (kids > 0 && !confirm('Remove “' + items[i].label + '”? Its ' + kids + (kids === 1 ? ' sub-item moves' : ' sub-items move') + ' up a level.')) return;
            for (var k = i + 1; k < end; k++) items[k].depth--;
            items.splice(i, 1);
            normalize(); setDirty(true); render();
            return;
        }
        if (b.dataset.move) {
            var idx = +b.dataset.i, item = items[idx], move = b.dataset.move;
            var keepOpen = item.open;
            if (move === 'up') {
                // Jump above the previous item at the same or shallower depth (and its block).
                var target = idx - 1;
                while (target > 0 && items[target].depth > item.depth) target--;
                var block = moveBlock(idx, target);
                var d = (items[target - 1] ? items[target - 1].depth + 1 : 0);
                if (block[0].depth > d) shiftDepth(block, d - block[0].depth);
            } else if (move === 'down') {
                var end2 = blockEnd(idx), nextEnd = blockEnd(end2), len = end2 - idx;
                moveBlock(idx, nextEnd - len);
            } else if (move === 'in') {
                shiftDepth(items.slice(idx, blockEnd(idx)), 1);
            } else if (move === 'out') {
                var p = parentIndex(idx), pEnd = blockEnd(p);
                var blk = moveBlock(idx, pEnd - (blockEnd(idx) - idx));
                shiftDepth(blk, -1);
            } else if (move === 'top') {
                var blk2 = moveBlock(idx, 0);
                shiftDepth(blk2, -blk2[0].depth);
            }
            normalize(); setDirty(true); render();
            var again = items.indexOf(item);
            if (keepOpen) { var bt = listEl.querySelector('.mb-item[data-i="' + again + '"] [data-move]'); if (bt) bt.focus(); }
        }
    });

    // ---------- Drag and drop (vertical = order, horizontal = level) ----------
    var drag = null;
    listEl.addEventListener('pointerdown', function (e) {
        var h = e.target.closest('[data-handle]');
        if (!h || e.button !== 0) return;
        e.preventDefault();
        var i = +h.closest('.mb-item').dataset.i;
        var end = blockEnd(i);
        drag = {
            block: items.slice(i, end),
            startX: e.clientX,
            startDepth: items[i].depth,
            relLast: items[end - 1].depth - items[i].depth,
            relMax: Math.max.apply(null, items.slice(i, end).map(function (x) { return x.depth - items[i].depth; })),
        };
        drag.block.forEach(function (x) { x.open = false; });
        root.classList.add('mb-dragging');
        render(); mark();
        document.addEventListener('pointermove', onMove);
        document.addEventListener('pointerup', onUp, { once: true });
    });

    function mark() {
        drag.block.forEach(function (x) {
            var el = listEl.querySelector('.mb-item[data-i="' + items.indexOf(x) + '"]');
            if (el) el.classList.add('lifted');
        });
    }

    function onMove(e) {
        if (!drag) return;
        var rest = items.filter(function (x) { return drag.block.indexOf(x) < 0; });
        // Where does the pointer sit among the items that aren't being dragged?
        var els = [].filter.call(listEl.children, function (el) { return !el.classList.contains('lifted'); });
        var pos = 0;
        els.forEach(function (el) { var r = el.getBoundingClientRect(); if (e.clientY > r.top + r.height / 2) pos++; });
        var prev = rest[pos - 1], next = rest[pos];
        var want = drag.startDepth + Math.round((e.clientX - drag.startX) / INDENT);
        var maxD = Math.min(prev ? prev.depth + 1 : 0, MAX - drag.relMax);
        var minD = next ? Math.max(0, next.depth - 1 - drag.relLast) : 0;
        var depth = Math.max(minD, Math.min(want, maxD));
        if (depth < 0) depth = 0;

        var delta = depth - drag.block[0].depth;
        var newOrder = rest.slice(0, pos).concat(drag.block, rest.slice(pos));
        var changedOrder = newOrder.some(function (x, k) { return x !== items[k]; });
        if (changedOrder || delta !== 0) {
            shiftDepth(drag.block, delta);
            items = newOrder;
            drag.moved = true;
            render(); mark();
        }
    }
    function onUp() {
        document.removeEventListener('pointermove', onMove);
        root.classList.remove('mb-dragging');
        if (drag && drag.moved) { normalize(); setDirty(true); }
        drag = null;
        render();
    }

    // ---------- Adding items ----------
    function addItem(it) {
        items.push({ label: it.label, url: it.url, newTab: false, depth: 0, linkType: it.linkType, linkId: it.linkId, original: it.original, open: false });
    }
    var PATH = { page: function (s) { return '/' + s.slug; }, post: function (s) { return '/' + s.slug; },
                 category: function (s) { return '/category/' + s.slug; }, tag: function (s) { return '/tag/' + s.slug; } };

    root.querySelectorAll('[data-source]').forEach(function (box) {
        var type = box.dataset.source;
        if (type === 'custom') return;
        var src = (data.sources[type] || []);
        var list = box.querySelector('[data-checklist]');
        if (!list) return;
        list.innerHTML = src.map(function (s, k) {
            return '<label class="check" data-name="' + esc(s.title.toLowerCase()) + '"><input type="checkbox" value="' + k + '"> ' + esc(s.title) + '</label>';
        }).join('');
        var filter = box.querySelector('[data-source-filter]');
        if (filter) filter.addEventListener('input', function () {
            var q = filter.value.trim().toLowerCase();
            list.querySelectorAll('label').forEach(function (l) { l.hidden = q && l.dataset.name.indexOf(q) < 0; });
        });
        box.querySelector('[data-select-all]').addEventListener('click', function () {
            var boxes = [].filter.call(list.querySelectorAll('input'), function (c) { return !c.closest('label').hidden; });
            var all = boxes.every(function (c) { return c.checked; });
            boxes.forEach(function (c) { c.checked = !all; });
        });
        box.querySelector('[data-add-selected]').addEventListener('click', function () {
            var picked = [].filter.call(list.querySelectorAll('input'), function (c) { return c.checked; });
            if (!picked.length) return;
            picked.forEach(function (c) {
                var s = src[+c.value];
                addItem({ label: s.title, url: PATH[type](s), linkType: type, linkId: s.id, original: s.title });
                c.checked = false;
            });
            setDirty(true); render();
            listEl.lastElementChild && listEl.lastElementChild.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        });
    });

    var urlIn = root.querySelector('[data-custom-url]'), labelIn = root.querySelector('[data-custom-label]');
    var customErr = root.querySelector('[data-custom-error]');
    root.querySelector('[data-add-custom]').addEventListener('click', function () {
        var url = urlIn.value.trim(), label = labelIn.value.trim();
        if (!/^(\/(?!\/)|#|https?:\/\/|mailto:|tel:)/i.test(url)) { customErr.textContent = 'Start the link with /, https://, mailto: or tel:.'; urlIn.focus(); return; }
        if (!label) { customErr.textContent = 'Give the link a label.'; labelIn.focus(); return; }
        customErr.textContent = '';
        addItem({ label: label, url: url, linkType: 'custom', linkId: null });
        urlIn.value = ''; labelIn.value = '';
        setDirty(true); render();
    });
    [urlIn, labelIn].forEach(function (el) { el.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); root.querySelector('[data-add-custom]').click(); } }); });

    // ---------- Saving ----------
    function save() {
        var blank = items.findIndex(function (x) { return !String(x.label).trim(); });
        if (blank >= 0) { items[blank].open = true; render(); stateEl.textContent = 'Every item needs a label.'; return; }
        saveBtn.disabled = true;
        stateEl.textContent = 'Saving…';
        fetch('/admin/menus/save', {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: JSON.stringify({ location: data.location, items: items.map(function (x) {
                return { label: x.label, url: x.url, newTab: !!x.newTab, depth: x.depth, linkType: x.linkType, linkId: x.linkId };
            }) }),
        }).then(function (res) {
            return res.json().catch(function () { return {}; }).then(function (j) { return { ok: res.ok, j: j }; });
        }).then(function (r) {
            saveBtn.disabled = false;
            if (!r.ok) { stateEl.textContent = r.j.error || 'Saving failed. Try again.'; stateEl.classList.add('mb-unsaved'); return; }
            var openSet = items.map(function (x) { return x.open; });
            items = r.j.items.map(function (i, k) {
                return { label: i.label, url: i.url, newTab: i.newTab, depth: i.depth, linkType: i.linkType, linkId: i.linkId, original: i.original, open: !!openSet[k] };
            });
            setDirty(false);
            stateEl.classList.remove('mb-unsaved');
            stateEl.textContent = 'Menu saved';
            render();
            var tab = document.querySelector('.filters a.active .n');
            if (tab) tab.textContent = items.length;
        }).catch(function () { saveBtn.disabled = false; stateEl.textContent = 'Saving failed. Check your connection.'; });
    }
    saveBtn.addEventListener('click', save);
    document.addEventListener('keydown', function (e) { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); save(); } });
    window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

    normalize();
    render();
})();
