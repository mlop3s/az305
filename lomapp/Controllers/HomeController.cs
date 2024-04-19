using lomapp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Azure.Storage.Blobs;

namespace lomapp.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<HomeController> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public HomeController(ILogger<HomeController> logger, GraphServiceClient graphServiceClient, BlobServiceClient blobServiceClient)
        {
            _logger = logger;
            _graphServiceClient = graphServiceClient; ;
            _blobServiceClient = blobServiceClient;
        }

        [AuthorizeForScopes(ScopeKeySection = "MicrosoftGraph:Scopes")]
        public async Task<IActionResult> Index()
        {
            var user = await _graphServiceClient.Me.Request().GetAsync();
            ViewData["GraphApiResult"] = user.DisplayName;
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public async Task<IActionResult> UploadFile(IFormFile model)
        {
            var userId = User?.GetObjectId();

            if (string.IsNullOrEmpty(userId))
            {
                ViewData["UploadResult"] = $"User has no id {User?.GetDisplayName()}";
                return View();
            }

            var clientReference = _blobServiceClient.GetBlobContainerClient(userId);

            _ = await clientReference.CreateIfNotExistsAsync();

            var fileName = BuildBlobName(model.FileName);

            var blobClient = clientReference.GetBlobClient(fileName);

            var result = await blobClient.UploadAsync(model.OpenReadStream());

            var status = result.GetRawResponse().Status;

            if (!IsSuccessStatusCode(status))
            {
                ViewData["UploadResult"] = "Upload failed";
                return View();
            }

            ViewData["UploadResult"] = fileName;
            return View("Index");
        }

        private static bool IsSuccessStatusCode(int statusCode)
        {
            return statusCode >= 200 && statusCode <= 299;
        }

        private static string BuildBlobName(string file)
        {
            var filename = Path.GetFileName(file);
            var ext = Path.GetExtension(file);
            return $"{filename}_{Guid.NewGuid()}{ext}";
        }
    }
}
