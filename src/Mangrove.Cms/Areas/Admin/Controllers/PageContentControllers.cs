using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mangrove.Cms.Areas.Admin.Controllers;

/// <summary>
/// Table pages and notice boards are edited inside the page editor (PostsController).
/// This controller only reads uploaded spreadsheets for the table grid, without saving anything,
/// so importing also works on a page that hasn't been saved yet.
/// </summary>
[Authorize(Policy = Policies.Editor)]
public class PageTableController : AdminController
{
    [HttpPost]
    public IActionResult Parse(IFormFile? file)
    {
        if (file == null) return BadRequest(new { error = "Choose an Excel or CSV file." });
        var (sheet, error) = TableService.ReadSpreadsheet(file);
        if (sheet == null) return BadRequest(new { error });

        var types = sheet.Headers.Select((h, i) => (int)TableService.GuessType(h, sheet.Rows.Select(r => i < r.Count ? r[i] : ""))).ToList();
        return Json(new
        {
            headers = sheet.Headers,
            types,
            rows = sheet.Rows.Select(r => sheet.Headers.Select((_, i) => i < r.Count ? r[i] : "").ToList()),
        });
    }

    // Old links from the first version of this feature.
    [HttpGet]
    public IActionResult Edit(int id) => RedirectToAction("Edit", "Posts", new { id });
}

[Authorize(Policy = Policies.Editor)]
public class NoticesController : AdminController
{
    [HttpGet]
    public IActionResult Index(int id) => RedirectToAction("Edit", "Posts", new { id });
}
