/* Mangrove CMS: menu builder. Dragging uses SortableJS (wwwroot/lib/sortablejs, MIT) with nested lists:
   every item has its own list of sub-items, and all lists share one Sortable group, so an item can be
   dropped between items or into another item's sub-list.
   State is a flat list where `depth` says how far each item is nested (0 = top level); it is turned into
   nested lists to render and read back from the DOM after each drop. */
(function () {
    'use strict';
    var data = window.MANGROVE_MENU;
    var root = document.getElementById('menu-builder');
    if (!data || !root || !window.Sortable) return;

    var MAX = data.maxDepth;   // deepest allowed depth (2 = three levels)
    var TYPE_LABEL = { custom: 'Custom link', page: 'Page', post: 'Post', category: 'Category', tag: 'Tag' };
    var items = data.items.map(function (i) {
        return { label: i.label, url: i.url, newTab: i.newTab, depth: i.depth, linkType: i.linkType, linkId: i.linkId, original: i.original, open: false };
    });
    var dirty = false, sortables = [];

    var listEl = root.querySelector('[data-list]'), emptyEl = root.querySelector('[data-empty]');
    var stateEl = root.querySelector('[data-state]'), saveBtn = root.querySelector('[data-save]');
    listEl.dataset.depth = '0';

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function token() { var el = document.querySelector('input[name="__RequestVerificationToken"]'); return el ? el.value : ''; }
    function setDirty(v) {
        dirty = v;
        if (v) stateEl.textContent = 'Unsaved changes';
        stateEl.classList.toggle('mb-unsaved', v);
    }

    // ---------- Tree helpers on the flat list ----------
    function blockEnd(i) { var j = i + 1; while (j < items.length && items[j].depth > items[i].depth) j++; return j; }
    function parentIndex(i) { for (var j = i - 1; j >= 0; j--) if (items[j].depth < items[i].depth) return j; return -1; }
    function prevSibling(i) {
        for (var j = i - 1; j >= 0; j--) { if (items[j].depth < items[i].depth) return -1; if (items[j].depth === items[i].depth) return j; }
        return -1;
    }
    /** How many levels an item's sub-tree reaches below it (0 = no sub-items). */
    function subtreeHeight(i) {
        var h = 0, end = blockEnd(i);
        for (var k = i + 1; k < end; k++) h = Math.max(h, items[k].depth - items[i].depth);
        return h;
    }
    function normalize() {
        var prev = -1;
        items.forEach(function (it) { it.depth = Math.max(0, Math.min(it.depth, prev + 1, MAX)); prev = it.depth; });
    }

    // ---------- Rendering: flat list -> nested lists ----------
    function itemHtml(i) {
        var it = items[i], parent = parentIndex(i), sib = prevSibling(i), isCustom = it.linkType === 'custom';
        var sub = it.depth > 0 ? '<span class="mb-sub">sub-item of ' + esc(items[parent].label) + '</span>' : '';
        var html = '<div class="mb-card">' +
            '<div class="mb-bar">' +
            '<span class="mb-handle" title="Drag to move" aria-hidden="true"><svg class="ic"><use href="#i-grip"/></svg></span>' +
            '<button type="button" class="mb-label" data-toggle="' + i + '" aria-expanded="' + it.open + '">' + esc(it.label) + sub + '</button>' +
            '<span class="mb-type">' + TYPE_LABEL[it.linkType] + '</span>' +
            '<button type="button" class="mb-chev" data-toggle="' + i + '" aria-label="Edit ' + esc(it.label) + '"><svg class="ic"><use href="#i-down"/></svg></button>' +
            '</div>';
        if (it.open) {
            html += '<div class="mb-detail">' +
                '<div class="mb-fields">' +
                '<div><label for="mb-l' + i + '">Label</label><input id="mb-l' + i + '" value="' + esc(it.label) + '" data-field="label" data-i="' + i + '"></div>' +
                '<div><label for="mb-u' + i + '">Link</label><input id="mb-u' + i + '" value="' + esc(it.url) + '" data-field="url" data-i="' + i + '"' + (isCustom ? '' : ' readonly') + '></div>' +
                '</div>' +
                (isCustom ? '' : '<p class="small muted">' + (it.original === 'Deleted'
                    ? 'The ' + TYPE_LABEL[it.linkType].toLowerCase() + ' this pointed to was deleted, so visitors won\'t see this item.'
                    : 'Links to the ' + TYPE_LABEL[it.linkType].toLowerCase() + ' “' + esc(it.original) + '”. The link updates if its address changes.') + '</p>') +
                '<label class="check"><input type="checkbox" data-field="newTab" data-i="' + i + '"' + (it.newTab ? ' checked' : '') + '> Open in a new tab</label>' +
                '<div class="mb-moves"><span class="small muted">Move</span>' +
                (i > 0 ? '<button type="button" class="linklike small" data-move="up" data-i="' + i + '">Up one</button>' : '') +
                (blockEnd(i) < items.length ? '<button type="button" class="linklike small" data-move="down" data-i="' + i + '">Down one</button>' : '') +
                (sib >= 0 && it.depth + subtreeHeight(i) < MAX ? '<button type="button" class="linklike small" data-move="in" data-i="' + i + '">Under ' + esc(items[sib].label) + '</button>' : '') +
                (it.depth > 0 ? '<button type="button" class="linklike small" data-move="out" data-i="' + i + '">Out from under ' + esc(items[parent].label) + '</button>' : '') +
                (i > 0 ? '<button type="button" class="linklike small" data-move="top" data-i="' + i + '">To the top</button>' : '') +
                '<span class="spacer"></span>' +
                '<button type="button" class="linklike small danger" data-remove="' + i + '">Remove</button>' +
                '</div></div>';
        }
        return html + '</div>';
    }

    /** Items from `start` while depth >= d, as <li>s; each li holds its card and its own sub-list. */
    function listHtml(start, end, depth) {
        var out = '', i = start;
        while (i < end) {
            var it = items[i], stop = blockEnd(i);
            out += '<li class="mb-item' + (it.open ? ' open' : '') + (it.original === 'Deleted' ? ' broken' : '') + '" data-i="' + i + '">' +
                itemHtml(i) +
                (depth < MAX ? '<ul class="mb-children" data-depth="' + (depth + 1) + '">' + listHtml(i + 1, stop, depth + 1) + '</ul>' : '') +
                '</li>';
            i = stop;
        }
        return out;
    }

    function render() {
        sortables.forEach(function (s) { s.destroy(); });
        sortables = [];
        normalize();
        emptyEl.hidden = items.length > 0;
        listEl.innerHTML = listHtml(0, items.length, 0);
        [listEl].concat([].slice.call(listEl.querySelectorAll('.mb-children'))).forEach(function (ul) {
            sortables.push(Sortable.create(ul, {
                group: 'mangrove-menu',
                handle: '.mb-handle',
                draggable: '.mb-item',
                animation: 150,
                fallbackOnBody: true,
                swapThreshold: 0.65,
                emptyInsertThreshold: 12,
                ghostClass: 'mb-ghost',
                chosenClass: 'mb-chosen',
                dragClass: 'mb-drag',
                onStart: onStart,
                onMove: onMove,
                onEnd: onEnd,
            }));
        });
    }

    // ---------- Drag and drop (SortableJS) ----------
    var dragHeight = 0;
    function onStart(evt) {
        dragHeight = subtreeHeight(+evt.item.dataset.i);
        root.classList.add('mb-sorting');
    }
    /** Refuse drops that would nest deeper than MAX levels (counting the dragged item's own sub-items). */
    function onMove(evt) {
        return +evt.to.dataset.depth + dragHeight <= MAX;
    }
    /** Read the new order and levels back from the nested lists. */
    function onEnd() {
        root.classList.remove('mb-sorting');
        var next = [];
        (function walk(ul, depth) {
            [].forEach.call(ul.children, function (li) {
                if (!li.classList.contains('mb-item')) return;
                var it = items[+li.dataset.i];
                it.depth = depth;
                next.push(it);
                var sub = li.querySelector(':scope > .mb-children');
                if (sub) walk(sub, depth + 1);
            });
        })(listEl, 0);
        var changed = next.length !== items.length || next.some(function (it, k) { return it !== items[k]; }) ||
            data._lastDepths !== next.map(function (x) { return x.depth; }).join(',');
        items = next;
        if (changed) setDirty(true);
        data._lastDepths = items.map(function (x) { return x.depth; }).join(',');
        render();
    }
    data._lastDepths = items.map(function (x) { return x.depth; }).join(',');

    // ---------- Editing ----------
    listEl.addEventListener('input', function (e) {
        var t = e.target, i = +t.dataset.i;
        if (!t.dataset.field || t.type === 'checkbox') return;
        items[i][t.dataset.field] = t.value;
        if (t.dataset.field === 'label') {
            var lbl = listEl.querySelector('.mb-item[data-i="' + i + '"] > .mb-card .mb-label');
            if (lbl) lbl.firstChild.textContent = t.value || '(no label)';
        }
        setDirty(true);
    });
    listEl.addEventListener('change', function (e) {
        var t = e.target;
        if (t.type === 'checkbox' && t.dataset.field) { items[+t.dataset.i][t.dataset.field] = t.checked; setDirty(true); }
    });

    function moveBlock(from, to) {
        var block = items.splice(from, blockEnd(from) - from);
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
            if (it.open) { var f = document.getElementById('mb-l' + b.dataset.toggle); if (f) f.focus(); }
            return;
        }
        if (b.dataset.remove !== undefined) {
            var i = +b.dataset.remove, end = blockEnd(i), kids = end - i - 1;
            if (kids > 0 && !confirm('Remove “' + items[i].label + '”? Its ' + kids + (kids === 1 ? ' sub-item moves' : ' sub-items move') + ' up a level.')) return;
            for (var k = i + 1; k < end; k++) items[k].depth--;
            items.splice(i, 1);
            setDirty(true); render();
            return;
        }
        if (b.dataset.move) {
            var idx = +b.dataset.i, item = items[idx], move = b.dataset.move;
            if (move === 'up') {
                var target = idx - 1;
                while (target > 0 && items[target].depth > item.depth) target--;
                var block = moveBlock(idx, target);
                var d = items[target - 1] ? items[target - 1].depth + 1 : 0;
                if (block[0].depth > d) shiftDepth(block, d - block[0].depth);
            } else if (move === 'down') {
                var end2 = blockEnd(idx), len = end2 - idx;
                moveBlock(idx, blockEnd(end2) - len);
            } else if (move === 'in') {
                shiftDepth(items.slice(idx, blockEnd(idx)), 1);
            } else if (move === 'out') {
                var p = parentIndex(idx), blk = moveBlock(idx, blockEnd(p) - (blockEnd(idx) - idx));
                shiftDepth(blk, -1);
            } else if (move === 'top') {
                var blk2 = moveBlock(idx, 0);
                shiftDepth(blk2, -blk2[0].depth);
            }
            setDirty(true); render();
            var bt = listEl.querySelector('.mb-item[data-i="' + items.indexOf(item) + '"] [data-move]');
            if (bt) bt.focus();
        }
    });

    // ---------- Adding items ----------
    function addItem(it) {
        items.push({ label: it.label, url: it.url, newTab: false, depth: 0, linkType: it.linkType, linkId: it.linkId, original: it.original, open: false });
    }
    var PATH = { page: function (s) { return '/' + s.slug; }, post: function (s) { return '/' + s.slug; },
                 category: function (s) { return '/category/' + s.slug; }, tag: function (s) { return '/tag/' + s.slug; } };

    root.querySelectorAll('[data-source]').forEach(function (box) {
        var type = box.dataset.source;
        if (type === 'custom') return;
        var src = data.sources[type] || [];
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
            if (listEl.lastElementChild) listEl.lastElementChild.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        });
    });

    var urlIn = root.querySelector('[data-custom-url]'), labelIn = root.querySelector('[data-custom-label]');
    var customErr = root.querySelector('[data-custom-error]'), addCustom = root.querySelector('[data-add-custom]');
    addCustom.addEventListener('click', function () {
        var url = urlIn.value.trim(), label = labelIn.value.trim();
        if (!/^(\/(?!\/)|#|https?:\/\/|mailto:|tel:)/i.test(url)) { customErr.textContent = 'Start the link with /, https://, mailto: or tel:.'; urlIn.focus(); return; }
        if (!label) { customErr.textContent = 'Give the link a label.'; labelIn.focus(); return; }
        customErr.textContent = '';
        addItem({ label: label, url: url, linkType: 'custom', linkId: null });
        urlIn.value = ''; labelIn.value = '';
        setDirty(true); render();
    });
    [urlIn, labelIn].forEach(function (el) { el.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); addCustom.click(); } }); });

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
            data._lastDepths = items.map(function (x) { return x.depth; }).join(',');
            setDirty(false);
            stateEl.textContent = 'Menu saved';
            render();
            var tab = document.querySelector('.filters a.active .n');
            if (tab) tab.textContent = items.length;
        }).catch(function () { saveBtn.disabled = false; stateEl.textContent = 'Saving failed. Check your connection.'; });
    }
    saveBtn.addEventListener('click', save);
    document.addEventListener('keydown', function (e) { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); save(); } });
    window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

    render();
})();
