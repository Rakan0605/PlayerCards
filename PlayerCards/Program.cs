using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PlayerCards.Entities;
using PlayerCards.Data;
using PlayerCards.Services;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Razor;
using PlayerCards.Middleware;


var builder = WebApplication.CreateBuilder(args);

// Add Localization
builder.Services.AddLocalization(options => options.ResourcesPath = "Languages");

// Add MVC with localization
builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();


builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("en"),
        new CultureInfo("ar")
    };

    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // Cookie first, then QueryString (both enabled)
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
    options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
});

// Database connection
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Bind settings
builder.Services.Configure<RecaptchaSettings>(
    builder.Configuration.GetSection("GoogleReCaptcha"));

// HttpClient for validator
builder.Services.AddHttpClient<IRecaptchaValidator, RecaptchaValidator>();

builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();



// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

var app = builder.Build();

app.UseRequestLocalization();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseFileValidation(); // Custom middleware to validate uploaded files

app.UseRouting();

app.UseSession(); // Session before authentication if controllers use it for login
app.UseAuthentication();
app.UseAuthorization();

// Default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Ensure at least one SuperAdmin exists
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!context.UserAccounts.Any(u => u.Role == "SuperAdmin"))
    {
        context.UserAccounts.Add(new UserAccount
        {
            FirstName = "Root",
            LastName = "Admin",
            Email = "super@admin.com",
            UserName = "superadmin",
            Password = "superpass", // TODO: Hash this in production
            Role = "SuperAdmin",
            IsActive = true
        });
        context.SaveChanges();
    }
}

app.Run();
