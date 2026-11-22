# Session summary (2026-11-22)

Saved after a context compaction, as read.md rule 10 asks. Public file: no secrets or hosts.

## Built so far
- Admin and themes redesigned; admin pages use the full width.
- Three page kinds: Standard, Table (dynamic columns, Excel import, resizable columns, paging) and
  Notices (title, files, details page). Table and notice editors sit inside the page editor.
- WordPress-style menus (primary, footer) with nested items up to 3 levels, dragged with SortableJS
  (bundled in wwwroot/lib/sortablejs, MIT).

## Public IDs (this change)
- User, Post, Term, Comment, Media and Notice have a `Guid Uid` (unique index) next to the int primary key.
- Every URL, form action and record hidden field uses the Uid, e.g. `/admin/posts/edit/{uid}`,
  `/{slug}/notice/{uid}`, `#comment-{uid}`. Numeric IDs stay internal (foreign keys, JSON editors).
- Edit forms post `Uid` (Guid.Empty = new); the controller resolves the int Id, which is `[BindNever]`.
- Migration `PublicUids` fills existing rows with random Uids before creating the unique indexes.

## Pending
- Name for publicly registered users ("registrant" or similar) still to be chosen.
