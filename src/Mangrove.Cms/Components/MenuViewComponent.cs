using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Mvc;

namespace Mangrove.Cms.Components;

/// <summary>Usage in a theme: @await Component.InvokeAsync("Menu", new { location = "primary" })</summary>
public class MenuViewComponent : ViewComponent
{
    private readonly MenuService _menus;
    public MenuViewComponent(MenuService menus) => _menus = menus;

    public async Task<IViewComponentResult> InvokeAsync(string location = "primary")
    {
        ViewData["Location"] = location;
        return View(await _menus.TreeAsync(location));
    }
}
