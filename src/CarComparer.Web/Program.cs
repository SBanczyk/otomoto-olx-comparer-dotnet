using CarComparer.Scraping;
using CarComparer.Web.Authorization;
using CarComparer.Web.Data;
using CarComparer.Web.Identity;
using CarComparer.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizePage("/Comparisons/Mine");
    options.Conventions.AuthorizeAreaFolder("Identity", "/Account/Manage");
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.")));

builder.Services
    // AddIdentity (not AddDefaultIdentity) so the Identity UI package's own pages are not served: only the scaffolded,
    // translated pages under Areas/Identity exist.
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddErrorDescriber<PolishIdentityErrorDescriber>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<IAuthorizationHandler, ComparisonOwnerAuthorizationHandler>();

builder.Services.AddListingScraping();
builder.Services.AddScoped<ListingService>();
builder.Services.AddScoped<ComparisonService>();
builder.Services.Configure<ComparisonsOptions>(builder.Configuration.GetSection(ComparisonsOptions.SectionName));
builder.Services.AddScoped<ComparisonCleanup>();
builder.Services.AddHostedService<ComparisonCleanupService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development only: apply migrations on startup.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

public partial class Program;


