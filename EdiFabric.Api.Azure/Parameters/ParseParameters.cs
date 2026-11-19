using EdiFabric.Native.X12;

namespace EdiFabric.Api.Azure.Models
{
    /// <summary>The query parameters of POST /x12/parse, as in ediFabric Cloud v3.</summary>
    public class ParseParameters
    {
        /// <summary>1 (Json), 2 (JsonValidate) or 3 (JsonValidateAck), as the number or the name. The default is 1.</summary>
        public string? Mode { get; set; }
        /// <summary>The encoding of the X12 file. The default is utf-8.</summary>
        public string? CharSet { get; set; }
        /// <summary>The validation and acknowledgment options as JSON: {"validate": {...}, "ack": {...}}.</summary>
        public string? Config { get; set; }

        public ParseMode ToMode()
        {
            if (string.IsNullOrEmpty(Mode))
                return ParseMode.Json;

            if (Enum.TryParse<ParseMode>(Mode, true, out var mode) && Enum.IsDefined(mode))
                return mode;

            throw new InvalidDataException($"Invalid mode '{Mode}'. Use 1 (Json), 2 (JsonValidate) or 3 (JsonValidateAck).");
        }
    }
}
