using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;
using OCAutomatica.Api.Parts;
using OCAutomatica.Api.PurchaseOrders;
using OCAutomatica.Api.Vendors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<EpicorOptions>(
    builder.Configuration.GetSection(EpicorOptions.SectionName));

builder.Services.AddHttpClient<IEpicorClient, EpicorClient>();

builder.Services.AddSingleton<ISessionStore, SessionStore>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBuyerService, BuyerService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<ICambiosFisicosService, CambiosFisicosService>();
builder.Services.AddScoped<IPartService, PartService>();
builder.Services.AddScoped<IVendorService, VendorService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseMiddleware<SessionMiddleware>();
app.MapControllers();

app.Run();

// Allows Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<Program> to
// reference this top-level-statement entry point from the test project.
public partial class Program { }
