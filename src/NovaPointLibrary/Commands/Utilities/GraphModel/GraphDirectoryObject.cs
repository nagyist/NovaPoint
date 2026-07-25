using Newtonsoft.Json;

namespace NovaPointLibrary.Commands.Utilities.GraphModel
{
    internal class GraphDirectoryObject
    {
        [JsonProperty("@odata.type")]
        public string ODataType { get; set; } = string.Empty;

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        // Graph returns the derived type on a directoryObject lookup, e.g.
        // '#microsoft.graph.group', '#microsoft.graph.directoryRole'.
        internal bool IsDirectoryRole
        {
            get
            {
                return ODataType.EndsWith("directoryRole", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
