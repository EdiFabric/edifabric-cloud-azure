namespace EdiFabric.Api.Azure
{
    public class Configuration
    {
        //  The serial used when a request doesn't send one in Ocp-Apim-Subscription-Key. Change it to your serial.
        //  The demo serial below works without an account, with a small daily quota.
        public static string ApiKey = "bd96a836feca45cb91c86ee65d281f52";
        //  Optional path to edifabric-x12-tools.dll/.so/.dylib, or leave empty to probe the output folder
        public static string LibraryPath = "";
    }
}
