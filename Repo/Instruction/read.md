# Mangrove CMS (open source): project rules

1. Read this file first before working on the project.
2. This is a PUBLIC repository (github.com/Mahi-BD/Mangrove_CMS, MIT). Never commit secrets, server IPs,
   passwords, private DB credentials or client data. Not even in this folder.
3. Stack: ASP.NET Core 8 MVC + EF Core 8 + SQLite. The database file lives in App_Data/ (gitignored).
4. Database approach: **Code-first with EF Core migrations.** This differs from the other RampsBD projects (DB-first),
   because an open-source app has to create and upgrade its own SQLite schema on every install.
   Add schema changes with: `dotnet ef migrations add <Name> -o Data/Migrations` (run in src/Mangrove.Cms).
   No raw SQL in LINQ (no FromSqlRaw/ExecuteSqlRaw for queries).
5. No CDN libraries. All CSS/JS is local in wwwroot.
6. All stored content is rendered through Services/ContentRenderer (Markdown + HtmlSanitizer).
7. POST forms with a literal action="" need asp-antiforgery="true" (AutoValidateAntiforgeryToken is global).
8. After every change: `dotnet build` from the repo root must finish with 0 errors and 0 warnings.
9. Test files and scratch go in Repo/Temp (gitignored).
10. When the conversation is compacted, save a summary to Repo/Instruction/[date-time-id.md].
