#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarComparer.Web.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel(UserManager<IdentityUser> userManager) : PageModel
    {
        [Display(Name = "Nazwa użytkownika")]
        public string Username { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można załadować użytkownika o identyfikatorze '{userManager.GetUserId(User)}'.");
            }

            Username = await userManager.GetUserNameAsync(user);
            return Page();
        }
    }
}
