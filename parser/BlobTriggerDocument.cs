using System.IO;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Company.Function
{ 
    public class BlobTriggerDocument
    {
        private static readonly string CosmosEndpoint = Environment.GetEnvironmentVariable("AZURE_COSMOS_DB_NOSQL_ENDPOINT");
        private static readonly string CosmosKey = Environment.GetEnvironmentVariable("AccountKey");

        private readonly ILogger<BlobTriggerDocument> _logger;

        public BlobTriggerDocument(ILogger<BlobTriggerDocument> logger)
        {
            _logger = logger;
        }

        [Function(nameof(BlobTriggerDocument))]
        public async Task Run([BlobTrigger("files/{name}")] BlobClient blobClient, string name)
        {
            if (!name.EndsWith("docx"))
            {
                _logger.LogError($"No processing {name}");
                return;
            } 

            // get blob metadata

            var properties = await blobClient.GetPropertiesAsync();

            var metadata = properties.Value.Metadata;

            if (!metadata.ContainsKey("reference") || !metadata.ContainsKey("userId") || !metadata.ContainsKey("social"))
            {
                _logger.LogError($"Missing meta data for {name}");
                return;
            }

            var reference = metadata["reference"];
            var userId = metadata["userId"];
            var social = metadata["social"];

            var stream = await blobClient.OpenReadAsync();
            using var blobStreamReader = new StreamReader(stream);
            var content = await blobStreamReader.ReadToEndAsync();


            // save content to CosmosDB
            CosmosClient client = new(
                accountEndpoint: CosmosEndpoint,
                authKeyOrResourceToken: CosmosKey
            );

            Database database = client.GetDatabase("documents");
            Container container = database.GetContainer("lomdocs");

            var document = new Document
            {
                id = Guid.NewGuid().ToString(),
                name = name,
                content = content,
                reference = reference,
                userId = userId,
                social = social
            };

            var result = await container.CreateItemAsync(document);
            if (result.StatusCode == System.Net.HttpStatusCode.Created)
            {
                _logger.LogInformation($"C# Blob trigger function Processed blob\n Name: {name} \n response: {result.StatusCode}");
            }
            else
            {
                _logger.LogError($"Failed for {name} response: {result.StatusCode}");
            }  
        }
    }
}
