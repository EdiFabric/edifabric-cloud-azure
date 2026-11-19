namespace EdiFabric.Api.Azure.Models
{
    /// <summary>The query parameters of POST /x12/build, as in ediFabric Cloud v3.</summary>
    public class BuildParameters
    {
        /// <summary>The postfix after each segment separator, for example %0D%0A for a new line. By default, there is none.</summary>
        public string? Postfix { get; set; }
    }
}
