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
// IEmailQueueRepository, IPurchaseOrderReportService, IPurchaseOrderEmailService:
// registered by Tasks 3, 4, and 7 respectively, when each type is created —
// the test project references this whole assembly, so registering a type
// before it exists breaks every test in the solution, not just the new ones.

var app = builder.Build();

app.UseHttpsRedirection();
app.UseMiddleware<SessionMiddleware>();
app.MapControllers();

app.Run();

// Allows Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<Program> to
// reference this top-level-statement entry point from the test project.
public partial class Program { }
