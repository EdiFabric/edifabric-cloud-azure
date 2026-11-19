using System.Runtime.InteropServices;
using Azure.Monitor.OpenTelemetry.Exporter;
using EdiFabric.Api.Azure;
using EdiFabric.Native.X12;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var workerDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
if (string.IsNullOrEmpty(workerDirectory))
    workerDirectory = AppContext.BaseDirectory;

PrependNativeLibrarySearchPath(workerDirectory);

var host = new HostBuilder()
                .ConfigureFunctionsWebApplication()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<ILocalModelsService, LocalModelsService>();

                    //  Sends logs, traces and metrics to Application Insights when the Function App has one.
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
                        s.AddOpenTelemetry().UseFunctionsWorkerDefaults().UseAzureMonitorExporter();
                })
                .Build();

if (string.IsNullOrEmpty(Configuration.ApiKey))
    throw new Exception("No ApiKey configuration.");

var libraryPath = string.IsNullOrWhiteSpace(Configuration.LibraryPath) ? workerDirectory : Configuration.LibraryPath;
try
{
    EdiFabricX12.Load(libraryPath);
}
catch (DllNotFoundException ex)
{
    throw new InvalidOperationException(
        $"Unable to load '{NativeLibraryFileName()}' from '{workerDirectory}'. " +
        "Linux Azure Function Apps need edifabric-x12-tools.so in the published output " +
        "(place it in the project or repository root before publishing). " +
        "Windows Function Apps need edifabric-x12-tools.dll.",
        ex);
}

//  Local models in map/map.json, if there are any, then the license and model map of the configured serial.
var localModels = host.Services.GetRequiredService<ILocalModelsService>();
localModels.Init(Path.Combine(workerDirectory, "map", "map.json"));
SerialGate.Init(localModels, Configuration.ApiKey);

host.Run();

static void PrependNativeLibrarySearchPath(string directory)
{
    if (string.IsNullOrEmpty(directory))
        return;

    var variable = OperatingSystem.IsWindows() ? "PATH" : "LD_LIBRARY_PATH";
    var current = Environment.GetEnvironmentVariable(variable);
    var combined = string.IsNullOrEmpty(current) ? directory : directory + Path.PathSeparator + current;
    Environment.SetEnvironmentVariable(variable, combined);
}

static string NativeLibraryFileName()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return "edifabric-x12-tools.dll";
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return "edifabric-x12-tools.dylib";
    return "edifabric-x12-tools.so";
}
