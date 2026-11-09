using System.Security.Claims;
using CarComparer.Web.Authorization;
using CarComparer.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarComparer.Web.Pages.Comparisons;

public class DetailsModel(ComparisonService comparisons, IAuthorizationService authorization, TimeProvider time) : PageModel
{
    public ComparisonView? View { get; private set; }
    public IReadOnlyList<string> Notes { get; private set; } = [];
    public bool IsOwner { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        View = await comparisons.GetViewAsync(id, ct);
        if (View is null)
        {
            return NotFound();
        }

        IsOwner = (await authorization.AuthorizeAsync(User, View.Comparison, ComparisonOperations.Manage)).Succeeded;

        if (TempData["Notes"] is string notes)
        {
            Notes = notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRefreshAsync(string id, CancellationToken ct)
    {
        var result = await comparisons.RefreshAsync(id, ct);
        if (result is null)
        {
            return NotFound();
        }

        TempData["Notes"] = string.Join('\n', result.Skipped.Select(s =>
            $"Ogłoszenie {s.Position}: dane odświeżono {s.MinutesAgo} min temu."));
        return RedirectToPage(new { id });
    }

    /// <summary>Always creates a copy owned by the current user; never takes over someone else's or an anonymous comparison.</summary>
    public async Task<IActionResult> OnPostSaveAsync(string id, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = Url.Page("Details", new { id }) });
        }

        var copyId = await comparisons.SaveCopyAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        return copyId is null ? NotFound() : RedirectToPage(new { id = copyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken ct)
    {
        var comparison = await comparisons.FindAsync(id, ct);
        if (comparison is null || !(await authorization.AuthorizeAsync(User, comparison, ComparisonOperations.Manage)).Succeeded)
        {
            return NotFound();
        }

        await comparisons.DeleteAsync(id, ct);
        return RedirectToPage("Mine");
    }

    public int AgeInDays(DateTimeOffset fetchedAt) => (int)(time.GetUtcNow() - fetchedAt).TotalDays;
}


