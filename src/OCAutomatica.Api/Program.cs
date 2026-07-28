using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;
using OCAutomatica.Api.Parts;
using OCAutomatica.Api.PurchaseOrders;
using OCAutomatica.Api.Users;
using OCAutomatica.Api.Vendors;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF Community license — free for organizations under $1M USD annual
// revenue (confirmed applicable, see spec section 8). Must be set once
// before any PurchaseOrderPdfDocument.GeneratePdf() call, or QuestPDF throws.
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddControllers();
builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<EpicorOptions>(
    builder.Configuration.GetSection(EpicorOptions.SectionName));
builder.Services.Configure<EmailQueueOptions>(
    builder.Configuration.GetSection(EmailQueueOptions.SectionName));
builder.Services.Configure<EpicorTokenOptions>(
    builder.Configuration.GetSection(EpicorTokenOptions.SectionName));

builder.Services.AddHttpClient<IEpicorClient, EpicorClient>();

builder.Services.AddSingleton<ISessionStore, SessionStore>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBuyerService, BuyerService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<ICambiosFisicosService, CambiosFisicosService>();
builder.Services.AddScoped<IPartService, PartService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IPurchaseOrderHistoryService, PurchaseOrderHistoryService>();
builder.Services.AddScoped<IUserDirectoryService, UserDirectoryService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IEmailQueueRepository, EmailQueueRepository>();
builder.Services.AddSingleton<IEpicorTokenService, EpicorTokenService>();
builder.Services.AddScoped<IPurchaseOrderReportService, PurchaseOrderReportService>();
builder.Services.AddScoped<IPurchaseOrderEmailService, PurchaseOrderEmailService>();

var app = builder.Build();

app.UseHttpsRedirection();

// Serves the React build (copied into wwwroot/ as part of publishing — see
// docs/superpowers/specs/2026-07-24-publicacion-iis-design.md) so the API
// and the SPA are one site: no CORS, no reverse proxy for the frontend.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<SessionMiddleware>();
app.MapControllers();

// Any route that isn't an API route or a real static file falls back to
// index.html, so React Router-less client-side navigation (this app has
// none today, but this is also what makes a hard refresh on any URL work)
// doesn't 404. Must be registered after MapControllers so API routes are
// matched first.
app.MapFallbackToFile("index.html");

app.Run();

// Allows Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<Program> to
// reference this top-level-statement entry point from the test project.
public partial class Program { }
