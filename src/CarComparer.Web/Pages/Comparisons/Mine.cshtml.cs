using System.Security.Claims;
using CarComparer.Web.Authorization;
using CarComparer.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarComparer.Web.Pages.Comparisons;

public class MineModel(ComparisonService comparisons, IAuthorizationService authorization) : PageModel
{
    public IReadOnlyList<MyComparisonRow> Rows { get; private set; } = [];

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task OnGetAsync(CancellationToken ct) => Rows = await comparisons.ListForOwnerAsync(UserId, ct);

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken ct)
    {
        var comparison = await comparisons.FindAsync(id, ct);
        // Not the owner looks the same as not existing: we do not reveal that the ID is in use.
        if (comparison is null || !(await authorization.AuthorizeAsync(User, comparison, ComparisonOperations.Manage)).Succeeded)
        {
            return NotFound();
        }

        await comparisons.DeleteAsync(id, ct);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAllAsync(CancellationToken ct)
    {
        await comparisons.DeleteAllForOwnerAsync(UserId, ct);
        return RedirectToPage();
    }
}
