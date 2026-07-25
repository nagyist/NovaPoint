using Newtonsoft.Json;

namespace NovaPointLibrary.Commands.Utilities.GraphModel;

public class GraphDetectedApp
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonProperty("version")]
    public string Version { get; set; } = string.Empty;
    [JsonProperty("publisher")]
    public string Publisher { get; set; } = string.Empty;
    [JsonProperty("platform")]
    public string Platform { get; set; } = string.Empty;
    [JsonProperty("sizeInByte")]
    public long SizeInByte { get; set; } = -1L;
    [JsonProperty("deviceCount")]
    public int DeviceCount { get; set; } = 0;
}
