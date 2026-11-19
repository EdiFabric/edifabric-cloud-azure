# ediFabric Native API for Azure Functions

## 1. Overview
This example hosts [ediFabric Native](https://www.edifabric.com/edifabric-native.html) behind an Azure Functions HTTP API. It translates X12 EDI to JSON and back, validates transaction sets, and generates acknowledgments using the C# bindings from [edifabric-csharp-bindings](https://github.com/EdiFabric/edifabric-csharp-bindings).

The native library is a self-contained shared library. No `EdiFabric.Api` NuGet package, .NET EDI runtime, or JVM is required on the target machine beyond this isolated-process Azure Functions host.

> NOTE: The example is for .NET [isolated process function](https://docs.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide).

## 2. Requirements
- [Visual Studio](https://visualstudio.microsoft.com/vs/) or the .NET 10 SDK.
- If you don't have an [Azure subscription](https://docs.microsoft.com/en-us/azure/guides/developer/azure-developer-guide#understanding-accounts-subscriptions-and-billing), create an [Azure free account](https://azure.microsoft.com/free/?ref=microsoft.com&utm_source=microsoft.com&utm_medium=docs&utm_campaign=visualstudio) before you begin.
- [Download Postman](https://www.postman.com/downloads/) - it's an application to consume/test your API.
- The native library for your platform:

| Platform | File |
| --- | --- |
| Windows | `edifabric-x12-tools.dll` |
| Linux | `edifabric-x12-tools.so` |
| macOS | `edifabric-x12-tools.dylib` |

[Download **ediFabric Native** 1.2.0 or later](https://www.edifabric.com/docs/edifabric-native/download.html)

Put the library in the repository root or in `EdiFabric.Api.Azure`, or set `Configuration.LibraryPath` / `EDIFABRIC_X12_LIB`. The project copies it next to the worker on build and publish when it is found in those folders.

Linux Azure Function Apps cannot load the Windows `.dll`. Before publishing to Linux, place `edifabric-x12-tools.so` (glibc x64) in the project or repository root so it is included in the deployment package. Windows Function Apps need `edifabric-x12-tools.dll` instead.

- X12 test file(s). `samples/837p.txt` and the Postman collection in `samples` are ready to use. For more, use ours - [X12 HIPAA](https://support.edifabric.com/hc/en-us/sections/360001487352-X12-HIPAA-Files-Templates), [X12](https://support.edifabric.com/hc/en-us/sections/360005274077-X12-Files-Templates).

## 3. License
Set `Configuration.ApiKey` to your serial. Requests can also send their own serial in the `Ocp-Apim-Subscription-Key` header, as with ediFabric Cloud. The demo serial below works without an account, so you can try the API straight away. It has a small daily quota: when it's used up, the API answers 403 until the next day. For your own serial, [sign up free](https://www.edifabric.com/account.html) for the Community plan or [choose a plan](https://www.edifabric.com/pricing.html).

```
bd96a836feca45cb91c86ee65d281f52
```

The API licenses ediFabric Native with `ensure_token`, which keeps a token for each serial and refreshes it before it expires, so it needs ediFabric Native 1.2.0 or later. Serials without tokens, such as the demo serial and Community, are checked online on every operation.

ediFabric Native keeps one license and one model map per process, so `SerialGate` runs each request under its own serial and model map: requests for the same serial run in parallel, and a request for another serial waits for them, switches, and runs on its own. With `map/map.json`, its local models are used for every serial.

| Status | Meaning |
| --- | --- |
| 401 | The serial is missing or incorrect. |
| 403 | The daily quota is used up, or the plan doesn't cover this release or more machines. |
| 429 | Too many requests. |
| 503 | The license server can't be reached. |

### Before you expose the API
The functions use `AuthorizationLevel.Anonymous`, so you can try them locally. Before you deploy, change them to `AuthorizationLevel.Function` and send the function key, or put the Function App behind Azure API Management, so that only your callers can use your serial.

## 4. Setup
Rebuild the solution. If there are any build errors, contact us at https://support.edifabric.com/hc/en-us/requests/new for assistance.

The C# bindings live in `EdiFabric.Api.Azure/Native` (`NativeMethods.cs` and `EdiFabricX12.cs`), based on the [edifabric-csharp-bindings](https://github.com/EdiFabric/edifabric-csharp-bindings) repository, with `ParseUtf8` and `BuildUtf8`, which work on UTF-8 bytes without converting to strings.

By default the API uses the online spec service (`SetMap` with `"default": "<serial>"`). To use local JSON models instead, place a `map/map.json` next to the function project (see the bindings README for the map format).

The function app uses the [ASP.NET Core integration](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide#aspnet-core-integration) and reads each request body once into a pooled buffer. To run it locally, use a current version of [Azure Functions Core Tools](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local) v4. When the Function App has Application Insights (`APPLICATIONINSIGHTS_CONNECTION_STRING`), logs, traces and metrics go there through OpenTelemetry.

## 5. Getting started
Run the function app locally, then POST X12 EDI to:

| Endpoint | Native call |
| --- | --- |
| `POST /api/x12/read` | `EdiFabricX12.ParseUtf8` (JSON only) |
| `POST /api/x12/write` | `EdiFabricX12.BuildUtf8` |
| `POST /api/x12/validate` | `EdiFabricX12.ParseUtf8` (JSON + validation report) |
| `POST /api/x12/ack` | `EdiFabricX12.ParseUtf8` (JSON + validation + 999/997/TA1) |
| `POST /api/x12/parse` | `EdiFabricX12.ParseUtf8` (JSON, + validation report in mode 2, + 999/997/TA1 in mode 3) |
| `POST /api/x12/build` | `EdiFabricX12.BuildUtf8` |

`read`, `write`, `validate` and `ack` are the [ediFabric Cloud v2](https://www.edifabric.com/docs/edifabric-cloud/x12-operations.html) X12 operations, and `parse` and `build` are the v3 ones. Send a serial in the `Ocp-Apim-Subscription-Key` header, or leave it out to use the configured one.

### ediFabric Cloud v3: parse and build

- **Parse:** send the X12 file with `Content-Type: application/octet-stream`, up to 5 MB. `mode` is `1` (JSON), `2` (JSON and the validation report) or `3` (also the acknowledgment), as the number or the name. `charSet` is the file's encoding, and `config` holds the validation and acknowledgment options as URL-escaped JSON, for example `{"validate":{"snip_level":4},"ack":{"gen997":true}}`. In modes 2 and 3 the response is two JSON documents back to back: split it at the byte offset in the `X-EdiFabric-Offset` header.
- **Build:** send the JSON with `Content-Type: application/json`, up to 50 MB, and optionally `postfix`, for example `%0D%0A` for a new line after each segment. The segments are written as they are, so include `SE`, `GE` and `IEA` with the correct counts.
- Another `Content-Type` returns 415, and a larger body returns 413. Unlike ediFabric Cloud, there is no `model` parameter: models come from `map/map.json` or the online spec library.

```bash
curl -X POST "http://localhost:7071/api/x12/parse?mode=3" -H "Content-Type: application/octet-stream" -D headers.txt -o response.bin --data-binary @claim.837
```

## 6. Warranty
*The source code in these example projects is strictly for demonstrational purposes and is provided "AS IS" without warranty of any kind, whether expressed or implied, including but not limited to the implied warranties of merchantability and/or fitness for a particular purpose.*

## 7. Additional information

[ediFabric Native documentation](https://support.edifabric.com/hc/en-us/articles/37276016388125-Introduction)

[C# bindings](https://github.com/EdiFabric/edifabric-csharp-bindings)

[Support](https://support.edifabric.com/hc/en-us/requests/new)
### 2026 © EdiFabric
