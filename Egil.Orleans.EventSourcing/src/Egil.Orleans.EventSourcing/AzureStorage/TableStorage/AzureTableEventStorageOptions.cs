using Azure.Data.Tables;
using Orleans;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing.AzureStorage.TableStorage;

public class AzureTableEventStorageOptions
{
    public const string DefaultTableName = "events";
    public const int DefaultInitStage = ServiceLifecycleStage.ApplicationServices;
    private TableServiceClient? tableServiceClient;

    /// <summary>
    /// Table name where events is stored
    /// </summary>
    public string TableName { get; set; } = DefaultTableName;

    /// <summary>
    /// Stage of silo lifecycle where storage should be initialized.  Storage must be initialized prior to use.
    /// </summary>
    public int InitStage { get; set; } = DefaultInitStage;

    /// <summary>
    /// Configures options for JSON serialization, using default settings optimized for web scenarios. Allows
    /// customization of serialization behavior.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// Gets or sets the client used to access the Azure Table Service.
    /// </summary>
    public TableServiceClient TableServiceClient
    {
        get => tableServiceClient ?? throw new InvalidOperationException("TableServiceClient not assigned in AzureTableEventStorageOptions.");
        set => tableServiceClient = value;
    }
}
