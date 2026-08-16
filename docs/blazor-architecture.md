# Blazor Architecture Guide

## Overview

This document describes the architecture and structure of the Blazor WebAssembly application in the Esi.AI.LiteLlm project.

## Project Structure

```
src/Esi.AI.LiteLlm/
├── Esi.AI.LiteLlm/                    # Blazor WebAssembly client project
│   ├── Program.cs                    # Client-side entry point
│   ├── Components/                   # Razor component hierarchy
│   │   ├── Layout/
│   │   │   ├── MainLayout.razor      # Root layout with NavMenu
│   │   │   ├── NavMenu.razor         # Navigation menu
│   │   │   └── ReconnectModal.razor  # WebSocket reconnection dialog
│   │   └── Pages/
│   │       └── Home.razor            # Main application page
│   └── App.razor                     # Root component
├── Esi.AI.LiteLlm.Client/             # Shared client contracts
│   ├── Contracts/
│   │   └── ChatCompletionRequest.cs  # OpenAI-compatible request model
│   └── Program.cs                    # Client-side entry point
└── Esi.AI.LiteLlm.Server/             # Server-side API
    ├── Program.cs                    # Server entry point with API endpoints
    └── EchoChatCompletionProvider.cs # Default LLM provider
```

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    Browser (Blazor Wasm)                 │
│                                                         │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────┐   │
│  │ MainLayout   │  │  NavMenu     │  │   Pages      │   │
│  │ (root)       │  │ (navigation) │  │ (Home, etc)  │   │
│  └──────┬──────┘  └──────┬───────┘  └──────┬───────┘   │
│         │                │                  │           │
│  ┌──────▼────────────────▼──────────────────▼──────────┐ │
│  │              Client Components                        │ │
│  │  - State: StateManager, SignalR clients               │ │
│  │  - Services: ChatCompletionService                    │ │
│  └──────────────────────┬───────────────────────────────┘ │
│                         │ HTTP/WS                          │
└─────────────────────────┼─────────────────────────────────┘
                          │
┌─────────────────────────▼─────────────────────────────────┐
│                   Server (ASP.NET Core)                   │
│                                                         │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────┐   │
│  │ Program.cs  │  │ EchoProvider │  │ ChatAPI      │   │
│  │ (entry)     │  │ (LLM backend)│  │ (endpoints)  │   │
│  └─────────────┘  └──────────────┘  └──────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## Key Components

### 1. Blazor WebAssembly Hosting

The client project uses **Blazor WebAssembly** hosting, which compiles C# code to WebAssembly and runs it entirely in the browser.

- **Program.cs**: Entry point that configures the WebAssembly host
- **RootComponents**: Registers the root component and services
- **HostBuilder**: Configures routing, static files, and client-side services

### 2. Component Hierarchy

Components follow a tree structure with composition:

```
App.razor
└── MainLayout.razor
    ├── NavMenu.razor          # Navigation
    ├── ReconnectModal.razor   # WebSocket reconnection
    └── Pages/Home.razor       # Content area
```

### 3. Client-Server Communication

- **HTTP**: Standard REST-like endpoints for chat completions
- **WebSocket**: Streaming responses for real-time token generation
- **Contracts**: Shared `ChatCompletionRequest`/`ChatCompletionResponse` models

### 4. Service Layer

The `ChatCompletionService` acts as the abstraction layer between client components and the LLM provider:

- Manages provider lifecycle
- Handles error recovery
- Coordinates streaming responses
- Provides a unified interface for all providers

## Hosting Options

| Option | Description | Use Case |
|--------|-------------|----------|
| **Blazor WebAssembly** | Client-side compilation | Production deployments, low server cost |
| **Blazor Server** | Server-side rendering | Server-heavy apps, complex state |
| **Blazor Hybrid** | Hybrid rendering | Mixed client/server needs |

## Dependencies

- **Microsoft.AspNetCore.Components**: Blazor component library
- **Microsoft.AspNetCore.Components.WebAssembly.Hosting**: Wasm hosting
- **Microsoft.AspNetCore.Components.Web**: Web hosting
- **SignalR**: Real-time communication
- **System.Text.Json**: JSON serialization

## Configuration

### Client-Side (`Esi.AI.LiteLlm.Client/Program.cs`)

```csharp
var builder = WebAssemblyHostBuilder.Create();
builder.Services.AddBlazor();
builder.Services.AddHttpClient();
builder.Services.AddChatCompletionService();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRazorPages();
```

### Server-Side (`Esi.AI.LiteLlm.Server/Program.cs`)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddChatCompletionProvider();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
```

## Routing

Blazor uses a **component-based routing** system:

- Routes are defined in `app.razor`
- Navigation uses `@page` directives
- URL parameters are bound to route segments

## State Management

State is managed through:

- **Component State**: `State<...>` for local component state
- **Services**: Singleton services for shared state
- **SignalR**: Real-time state synchronization
- **Client-side Storage**: For persistent data

## Error Handling

- **Client**: Try/catch around HTTP calls, reconnection logic
- **Server**: Global exception handling, provider error mapping
- **UI**: User-friendly error messages and reconnection dialogs

## Security

- **CORS**: Configured for client-server communication
- **Authentication**: JWT-based API authentication
- **Input Validation**: Request validation on all endpoints
- **Rate Limiting**: Protection against abuse

## Testing

- **Unit Tests**: Service layer and provider implementations
- **Integration Tests**: Client-server communication
- **Component Tests**: Blazor component rendering
- **E2E Tests**: Full application flow

## Deployment

### Client (Blazor Wasm)

1. Build: `dotnet publish -c Release -o ./publish`
2. Deploy: Upload `publish` folder to web server
3. Serve: Static files with proper MIME types

### Server (ASP.NET Core)

1. Build: `dotnet publish -c Release -o ./publish`
2. Deploy: Run with Kestrel or IIS
3. Configure: Environment variables, logging, monitoring

## Performance

- **Blazor Wasm**: First load includes compilation overhead
- **Caching**: Static assets cached by browser
- **Streaming**: Token streaming reduces perceived latency
- **Compression**: Enable gzip/brotli for static assets

## Debugging

- **Client**: Browser DevTools, Blazor DevTools
- **Server**: Visual Studio debugger, Kestrel logs
- **Network**: Network tab for HTTP/WS inspection
- **Logs**: Application logs for error tracking

## References

- [Blazor WebAssembly Documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly)
- [ASP.NET Core Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor)
- [SignalR Documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/signalr)
- [Razor Components](https://learn.microsoft.com/en-us/aspnet/core/blazor/components)
