using System.Text.Json;
using System.Text.Json.Nodes;

namespace EdiFabric.Api.Azure
{
    public interface ILocalModelsService
    {
        /// <summary>Reads the local model map, map/map.json, when there is one. Without it, models come from the online spec library.</summary>
        void Init(string? mapPath);

        /// <summary>The model map for a serial, for set_map.</summary>
        string MapFor(string serial);
    }

    /// <summary>
    /// The model map for each serial. With map/map.json, its models are used, and the serial becomes the default for every
    /// other transaction unless the map names one. Without it, every model comes from the online spec library under that serial.
    /// </summary>
    public class LocalModelsService : ILocalModelsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private JsonObject? _localMap;

        public void Init(string? mapPath)
        {
            if (string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath))
            {
                _localMap = null;
                return;
            }

            var mapLocation = Path.GetDirectoryName(Path.GetFullPath(mapPath))
                ?? throw new InvalidOperationException($"Can't resolve the directory for map file '{mapPath}'.");

            var localMap = JsonNode.Parse(File.ReadAllText(mapPath))?.AsObject()
                ?? throw new InvalidDataException($"Map file '{mapPath}' is not a JSON object.");

            var maps = localMap["maps"]?.AsObject();
            if (maps is not null)
            {
                foreach (var entry in maps)
                {
                    if (entry.Value is JsonObject mapEntry)
                        mapEntry["location"] = mapLocation;
                }
            }

            _localMap = localMap;
        }

        public string MapFor(string serial)
        {
            if (_localMap is null)
                return new JsonObject { ["default"] = serial, ["maps"] = new JsonObject() }.ToJsonString(JsonOptions);

            var map = _localMap.DeepClone().AsObject();
            if (string.IsNullOrWhiteSpace(map["default"]?.GetValue<string>()))
                map["default"] = serial;

            return map.ToJsonString(JsonOptions);
        }
    }
}
