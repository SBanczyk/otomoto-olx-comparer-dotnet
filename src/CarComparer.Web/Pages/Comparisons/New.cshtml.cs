using System.Security.Claims;
using CarComparer.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarComparer.Web.Pages.Comparisons;

public class NewModel(ComparisonService comparisons) : PageModel
{
    [BindProperty]
    public List<string?> Urls { get; set; } = ["", ""];

    public Dictionary<int, string> FieldErrors { get; private set; } = [];
    public string? FormError { get; private set; }

    public void OnGet()
    {
    }

    public IActionResult OnPostAddField()
    {
        Normalise();
        if (Urls.Count < ComparisonService.MaxListings)
        {
            Urls.Add("");
        }

        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Normalise();
        var result = await comparisons.CreateAsync(Urls, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        if (result.Succeeded)
        {
            return RedirectToPage("Details", new { id = result.Id });
        }

        FieldErrors = result.FieldErrors.ToDictionary(e => MapToField(e.Key), e => e.Value);
        FormError = result.FormError;
        return Page();
    }

    private void Normalise()
    {
        Urls ??= [];
        if (Urls.Count > ComparisonService.MaxListings)
        {
            Urls = Urls.Take(ComparisonService.MaxListings).ToList();
        }

        while (Urls.Count < ComparisonService.MinListings)
        {
            Urls.Add("");
        }
    }

    // The service numbers errors by non-blank input; map back to the visible field index.
    private int MapToField(int nonBlankIndex) =>
        Urls.Select((u, i) => (u, i)).Where(x => !string.IsNullOrWhiteSpace(x.u)).Skip(nonBlankIndex).Select(x => x.i).FirstOrDefault();
}
