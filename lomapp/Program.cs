using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Files.Shares;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Graph.ExternalConnectors;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

var initialScopes = builder.Configuration["DownstreamApi:Scopes"]?.Split(' ') ?? builder.Configuration["MicrosoftGraph:Scopes"]?.Split(' ');

// Add services to the container.
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi(initialScopes)
            .AddMicrosoftGraph(builder.Configuration.GetSection("MicrosoftGraph"))
            .AddInMemoryTokenCaches();

builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});
builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

var connectionString = builder.Configuration.GetValue<string>("ConnectionStrings:AzureBlobStorage");

if (string.IsNullOrEmpty(connectionString))
{
    connectionString = RetrieveConnectionString();
}

builder.Services.AddScoped(x => new BlobServiceClient(connectionString));
builder.Services.AddScoped(x => new ShareClient(connectionString, "lomshare"));
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

static string RetrieveConnectionString()
{
    const string uri = "https://lomfortknox.vault.azure.net/";
    SecretClient client = new SecretClient(new Uri(uri), new DefaultAzureCredential());

    Response<KeyVaultSecret> secret = client.GetSecretAsync("lomstorageaccount").GetAwaiter().GetResult();

    var status = secret.GetRawResponse().Status;
    if (status != 200)
    {
        throw new InvalidOperationException("Failed to retrieve secret");
    }

    return secret.Value.Value;
}