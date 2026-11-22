# 🌿 Mangrove CMS

**A WordPress-style content management system built with ASP.NET Core and SQLite.**
It's a single, self-contained app: no database server to install, no Node build step, and no CDN dependencies.

[![build](https://github.com/Mahi-BD/Mangrove_CMS/actions/workflows/build.yml/badge.svg)](https://github.com/Mahi-BD/Mangrove_CMS/actions/workflows/build.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![SQLite](https://img.shields.io/badge/database-SQLite-003B57)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

## Features

| | |
|---|---|
| **Posts & pages** | Drafts, publishing, scheduled posts, sticky posts, trash and restore, nested pages, featured images, excerpts and SEO meta descriptions |
| **Page types** | *Standard* pages with text and images; *table pages* (officer lists, directories, price lists) with typed columns (serial no., text, number, phone, email, image, link, date), spreadsheet-style editing, drag-to-resize columns, Excel/CSV import, and paging or a single long page; *notice boards* with dated notices, multiple attached files per notice, and a details page for each |
| **Writing** | Markdown editor with a toolbar, live preview, keyboard shortcuts, and paste or drag-and-drop image upload |
| **Taxonomies** | Categories (hierarchical) and tags, each with an archive page |
| **Media library** | Upload, search, alt text and captions. File types are allow-listed, and SVG/HTML uploads are blocked |
| **Comments** | Threaded replies, a moderation queue (approve, spam, trash), a honeypot and rate limiting |
| **Users & roles** | Administrator, Editor, Author, Subscriber. Optional public registration |
| **Themes** | Razor themes with fallback: a theme overrides only the views it needs. Ships with *Mangrove* (magazine layout, Literata) and *Minimal* (single column) |
| **Menus** | Primary and footer menus, with quick-add for pages and categories |
| **SEO** | Clean `/slug` URLs (Unicode-friendly, e.g. Bangla), RSS feed (`/feed`), `sitemap.xml`, `robots.txt`, canonical and Open Graph tags |
| **Security** | PBKDF2 password hashing, antiforgery on every POST, sanitized HTML output, login rate limiting, and sessions revoked on password or role change |
| **Setup** | First-run wizard creates the admin account and optional sample content |

## Design

The admin and both themes use self-hosted open-source fonts ([Literata](https://github.com/googlefonts/literata) for reading, [Schibsted Grotesk](https://github.com/schibsted/schibsted-grotesk) for the interface; SIL OFL 1.1, in `wwwroot/fonts`). The palette comes from the mangrove forest: tidewater green, brackish teal and tannin amber. Light and dark mode follow the visitor's system setting.

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/Mahi-BD/Mangrove_CMS.git
cd Mangrove_CMS
dotnet run --project src/Mangrove.Cms
```

Open **http://localhost:5080**. The setup wizard asks for a site title and an administrator account.
The admin dashboard lives at **/admin**.

### Docker

```bash
docker build -t mangrove-cms .
docker run -d -p 8080:8080 -v mangrove-data:/data -v mangrove-uploads:/app/wwwroot/uploads mangrove-cms
```

## Deploying

```bash
dotnet publish src/Mangrove.Cms -c Release -o /var/www/mangrove
```

Run it behind nginx or another reverse proxy (`X-Forwarded-*` headers are honoured). **Back up these two
locations, and keep them when you upgrade:**

| Path | Contents |
|---|---|
| `App_Data/` | `mangrove.db` (all content and settings) and `keys/` (login cookie keys) |
| `wwwroot/uploads/` | Uploaded media |

The data folder can be moved with the `Mangrove__DataDirectory` environment variable (or `Mangrove:DataDirectory`
in `appsettings.json`). Database migrations apply automatically on start.

## Roles

| Role | Can |
|---|---|
| Subscriber | Edit their own profile, comment |
| Author | Write, publish and trash **their own** posts, upload media |
| Editor | Manage all posts, pages, categories, tags, comments and menus |
| Administrator | Everything, plus users, settings and themes |

## Themes

```
src/Mangrove.Cms/
├── Themes/
│   ├── Mangrove/            ← default theme (the full set of views)
│   │   ├── theme.json
│   │   ├── _Layout.cshtml   _Footer.cshtml   _PostCard.cshtml   _PostMeta.cshtml
│   │   ├── Index.cshtml     ← home (lead story + list), blog, archives and search
│   │   ├── Single.cshtml    ← posts and pages
│   │   └── NotFound.cshtml  Error.cshtml
│   └── Minimal/             ← overrides only _Layout + CSS
└── wwwroot/themes/<name>/   ← theme CSS, JS and images
```

Activate a theme from **Admin → Themes**. Views are looked up in the active theme, then in *Mangrove*.
A theme can also override account pages (`Themes/<Name>/Account/Login.cshtml`) and the menu
(`Themes/<Name>/Components/Menu/Default.cshtml`).

## Project layout

```
src/Mangrove.Cms/
├── Areas/Admin/      dashboard controllers + views
├── Controllers/      public site, account, setup wizard
├── Data/             EF Core DbContext + migrations
├── Models/           entities + view models
├── Services/         settings, slugs, Markdown rendering, media, themes, auth
├── Themes/           public-site themes
└── wwwroot/          admin CSS/JS, theme assets, uploads
```

## Roadmap

- [ ] Revisions / autosave
- [ ] Image resizing and thumbnails
- [ ] Custom fields and custom post types
- [ ] Plugin/hook system
- [ ] WordPress WXR import
- [ ] Multi-language content

## Contributing

PRs welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
