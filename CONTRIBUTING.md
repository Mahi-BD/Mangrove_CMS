# Contributing to Mangrove CMS

Thanks for helping! Bug reports, ideas, themes and pull requests are all welcome.

## Getting started

```bash
git clone https://github.com/Mahi-BD/Mangrove_CMS.git
cd Mangrove_CMS
dotnet run --project src/Mangrove.Cms
```

Open http://localhost:5080 and finish the setup wizard. The SQLite database is created in
`src/Mangrove.Cms/App_Data/`. Delete that folder to start over.

## Guidelines

- **Keep it dependency-light.** The admin and themes use plain CSS/JS with no CDNs. Add a NuGet package only when it clearly earns its place.
- **Schema changes go through EF Core migrations:**
  `dotnet ef migrations add <Name> --project src/Mangrove.Cms -o Data/Migrations`
- **Everything user-authored is rendered through `ContentRenderer`** (Markdown → sanitized HTML). Never output stored content with `Html.Raw`.
- **Every POST form needs an antiforgery token.** Forms with a literal `action="..."` need `asp-antiforgery="true"`.
- Make sure `dotnet build -warnaserror` passes before you open a PR.

## Writing a theme

Copy `Themes/Minimal` to start. A theme only needs a `theme.json` plus the views it overrides
(`_Layout`, `Index`, `Single`, `_PostCard`, `_Sidebar`, `NotFound`, `Account/Login`, …).
Static files go in `wwwroot/themes/<name>/`. Anything you don't override falls back to the default *Mangrove* theme.
