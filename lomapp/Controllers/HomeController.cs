using lomapp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Azure.Storage.Blobs;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Files.Shares;
using Azure;
using Microsoft.ApplicationInsights;
using Microsoft.IdentityModel.Abstractions;

namespace lomapp.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<HomeController> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ShareClient _shareClient;
        private readonly string _context;
        private readonly string _functionEndpoint;
        private readonly TelemetryClient _telemetryClient;

        public HomeController(ILogger<HomeController> logger,
                              GraphServiceClient graphServiceClient,
                              BlobServiceClient blobServiceClient,
                              ShareClient shareClient,
                              TelemetryClient telemetryClient,
                              IConfiguration configuration)
        {
            _logger = logger;
            _graphServiceClient = graphServiceClient; 
            _blobServiceClient = blobServiceClient;
            _shareClient = shareClient;
            _context = configuration.GetValue<string>("RequestContext");
            _functionEndpoint = configuration.GetValue<string>("FunctionEndpoint");
            _telemetryClient = telemetryClient;
        }



        [AllowAnonymous]
        public IActionResult Index()
        {
            return View();
        }


        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [AuthorizeForScopes(ScopeKeySection = "MicrosoftGraph:Scopes")]
        public async Task<IActionResult> Upload()
        {
            var user = await _graphServiceClient.Me.Request().GetAsync();
            ViewData["GraphApiResult"] = user.DisplayName;
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private IActionResult ViewUpload()
        {
            return View("Upload");
        }

        public async Task<IActionResult> UploadFile(IFormFile model, string social)
        {
            if (!string.IsNullOrEmpty(social))
            {
                ViewData["UploadResult"] = $"No social number";
                return ViewUpload();
            }

            var userId = User?.GetObjectId();

            if (string.IsNullOrEmpty(userId))
            {
                ViewData["UploadResult"] = $"User has no id {User?.GetDisplayName()}";
                return ViewUpload();
            }

            // call a azure function using post and entra id authentication

            var ok = await EnsureBlobContainer(userId, _context, _functionEndpoint);

            if (!ok.Item2)
            {
                ViewData["UploadResult"] = "Failed to create container";
                return ViewUpload();
            }

            var clientReference = _blobServiceClient.GetBlobContainerClient(userId);

            _ = await clientReference.CreateIfNotExistsAsync();

            var fileName = BuildBlobName(model.FileName);

            var reference = Guid.NewGuid().ToString();

            IDictionary<string, string> metadata =
               new Dictionary<string, string>
               {
                   { "reference", reference },
                   { "userId", userId },
                   { "social", social }
               };

            var blobClient = clientReference.GetBlobClient(fileName);

            var metaResult = await blobClient.SetMetadataAsync(metadata);

            if (!IsSuccessStatusCode(metaResult.GetRawResponse().Status))
            {
                ViewData["UploadResult"] = "Metadata failed";
                return ViewUpload();
            }

            var result = await blobClient.UploadAsync(model.OpenReadStream());

            var status = result.GetRawResponse().Status;

            if (!IsSuccessStatusCode(status))
            {
                ViewData["UploadResult"] = "Upload failed";
                return ViewUpload();
            }

            ViewData["UploadResult"] = $"Reference: {reference} for {fileName}";
            return ViewUpload();
        }

        public async Task<IActionResult> UploadFileToShare(IFormFile model)
        {
            var userId = User?.GetObjectId();

            if (string.IsNullOrEmpty(userId))
            {
                ViewData["UploadResult"] = $"User has no id {User?.GetDisplayName()}";
                return View();
            }

            string dirName = userId;
            var fileName = BuildBlobName(model.FileName);


            var directory = _shareClient.GetDirectoryClient(dirName);
            await directory.CreateIfNotExistsAsync();

            var fileClient = directory.GetFileClient(fileName);

            //  Azure allows for 4MB max uploads  (4 x 1024 x 1024 = 4194304)
            const int uploadLimit = 4194304;
            long fileSize = model.Length;
            long offset = 0;

            await fileClient.CreateAsync(fileSize);

            using var uploadFileStream = model.OpenReadStream();

            while (offset < fileSize)
            {
                long bytesToRead = Math.Min(uploadLimit, fileSize - offset);
                byte[] blockBytes = new byte[bytesToRead];

                int bytesRead = await uploadFileStream.ReadAsync(blockBytes, 0, (int)bytesToRead);
                using MemoryStream blockStream = new MemoryStream(blockBytes, 0, bytesRead);

                var response = await fileClient.UploadRangeAsync(
                    new HttpRange(offset, bytesRead),
                    blockStream);

                var status = response.GetRawResponse().Status;

                if (!IsSuccessStatusCode(status))
                {
                    ViewData["UploadResult"] = "Upload failed";
                    return View();
                }

                offset += bytesRead;
            }

            ViewData["UploadResult"] = fileName;

            // add custom telemetry files uploaded count
            _telemetryClient.TrackEvent("FileUploaded", new Dictionary<string, string> { { "FileName", fileName } });


            return View("Upload");
        }

        private static async Task<(string, bool)> EnsureBlobContainer(string userId, string context, string endPoint)
        {
            var credential = new DefaultAzureCredential();
            var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { context }));

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

                var userRequest = new
                {
                    userId = userId
                };

                var response = await client.PostAsJsonAsync(endPoint, userRequest);

                string responseString = await response.Content.ReadAsStringAsync();

                return (responseString, response.IsSuccessStatusCode);
            }
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
