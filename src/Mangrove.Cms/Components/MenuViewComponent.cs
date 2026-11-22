using Mangrove.Cms.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Cms.Components;

/// <summary>Usage in a theme: @await Component.InvokeAsync("Menu", new { location = "primary" })</summary>
public class MenuViewComponent : ViewComponent
{
    private readonly CmsDbContext _db;
    public MenuViewComponent(CmsDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync(string location = "primary")
    {
        var items = await _db.MenuItems.AsNoTracking()
            .Where(m => m.Location == location).OrderBy(m => m.SortOrder).ToListAsync();
        ViewData["Location"] = location;
        return View(items);
    }
}
