# OpenWSDL

OpenWSDL downloads a **WSDL** (and related XSD imports), walks the SOAP bindings, and generates:

- **OpenAPI 3** JSON — one POST with **examples** per operation (good for Swagger-style tools; see notes for Postman).
- **Postman Collection v2.1** — **one request per SOAP operation**, with **Content-Type**, **SOAPAction**, and **indented** sample XML bodies.

Default titles use the WSDL **`wsdl:service`** name when present, otherwise the service host.

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) matching the project (e.g. **.NET 10** as in `OpenWSDL.csproj`).

## Authentication (NTLM / Windows integrated)

WSDL and schema downloads use an `HttpClient` configured with **`UseDefaultCredentials`**. When the server answers **401 Unauthorized** with **NTLM** or **Negotiate** (typical for IIS and internal `.svc` endpoints), the tool retries using the **current Windows user** (the account running `dotnet` or the IDE terminal).

- Run the tool **signed in as a user** that is allowed to reach the URL (same as you would use in a browser on that machine).
- On **Linux/macOS**, default credentials may not participate in Windows domain auth; use a machine joined to the domain or another approach if you hit 401 there.

There is no separate username/password flag; use Windows integrated auth or a URL that does not require it.

## Build and run

From the repository root:

```bash
dotnet build OpenWSDL/OpenWSDL.csproj
dotnet run --project OpenWSDL
```

Or run the built executable from `OpenWSDL/bin/Debug/net10.0/` (framework may vary).

## Interactive mode (recommended)

Run **with no command-line arguments**:

```bash
dotnet run --project OpenWSDL
```

You get a [Spectre.Console](https://spectreconsole.net/) flow: WSDL URL, multi-select outputs (OpenAPI and/or Postman), output folder, file base name, title, a path preview, and confirmation.

## Command-line usage

Show help:

```bash
dotnet run --project OpenWSDL -- --help
```

**Arguments and options**

| Item | Description |
|------|-------------|
| `wsdl-url` | Optional. Absolute `http` or `https` WSDL URL. If omitted, the CLI prompts on stdin. |
| `-o`, `--output` | Path to write **OpenAPI 3** JSON. |
| `-p`, `--postman` | Path to write **Postman Collection** JSON. |
| `--title` | Overrides **OpenAPI `info.title`** and **Postman `info.name`**. Default: service name or host. |

You can pass **both** `-o` and `-p` in one run.

**Examples**

```bash
# OpenAPI only
dotnet run --project OpenWSDL -- "https://example.com/Service.svc?wsdl" -o openapi.json

# Postman only (best for Postman: one request per operation, headers filled in)
dotnet run --project OpenWSDL -- "https://example.com/Service.svc?wsdl" -p MyService.postman_collection.json

# Both
dotnet run --project OpenWSDL -- "https://example.com/Service.svc?wsdl" -o openapi.json -p MyService.postman_collection.json

# Custom title
dotnet run --project OpenWSDL -- "https://example.com/Service.svc?wsdl" -p out.postman_collection.json --title "My API"
```

If you omit **both** `-o` and `-p`, OpenAPI JSON is written to **stdout**.

## Postman vs OpenAPI

| | **Postman collection (`-p`)** | **OpenAPI (`-o`)** |
|---|-------------------------------|---------------------|
| Requests | One **request per SOAP operation** | One **POST**; operations appear as **named examples** |
| URL | Full SOAP endpoint on each request | `servers` + path joined for the endpoint |
| Headers | **Content-Type** (`text/xml; charset=utf-8` for SOAP 1.1) and **SOAPAction** per request | Documented as parameters; body media type is `text/plain` in the spec so imports do not wrap XML incorrectly |

**Import the `.postman_collection.json` file** in Postman (Import → file). For the best experience with SOAP, prefer the collection over importing OpenAPI.

**SOAPAction** for SOAP 1.1 is emitted with **double quotes** around the action URI (typical for .NET **BasicHttpBinding**). If your server expects an unquoted value, edit the header in Postman.

SOAP **1.2** uses a single **Content-Type** header with `application/soap+xml` and an `action` parameter.

## What gets generated in the XML samples

- SOAP **1.1** envelope prefix **`soapenv`**, empty **Header**, **Body** with contract elements prefixed **`ns`** (or SOAP 1.2 equivalent).
- **XSD-aware** sample elements (simple types, nested structures, enums, optional handling, etc.).
- **Pretty-printed** XML (indented) in both Postman and OpenAPI outputs.

## Limitations (brief)

- Focused on **document/literal** SOAP with resolvable **`wsdl:part element="..."`** messages; split WSDLs with **`wsdl:import`** are supported when imports load successfully.
- **RPC/encoded** and exotic bindings may not convert fully.
- The first **SOAP port** found on the chosen WSDL is used.
- **SOAP headers** from the WSDL are not expanded into sample headers.

## Exit codes (CLI)

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Missing or invalid WSDL URL |
| 2 | No usable SOAP 1.1/1.2 binding / messages |
| 3 | Failed to load WSDL (network, parse, etc.) |

## Dependencies

- [Microsoft.OpenApi](https://www.nuget.org/packages/Microsoft.OpenApi) — OpenAPI document serialization  
- [System.CommandLine](https://www.nuget.org/packages/System.CommandLine) — CLI  
- [Spectre.Console](https://www.nuget.org/packages/Spectre.Console) — interactive UI  
