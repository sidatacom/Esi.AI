# Esi.AI

Esi.AI is a C# fork designed for seamless integration with VS Code and GitHub Copilot. It provides a robust framework for extending AI capabilities within the development environment.

## Project Overview

Esi.AI serves as a central hub for several key components and forks, enabling advanced AI-driven workflows:

- **DebugMCP**: Provides advanced debugging controls and integration for Model Context Protocol (MCP) servers.
- **Terminal MCP**: Enables the execution and management of commands directly within visible VS Code terminal tabs.
- **LiteLLM**: A C# implementation and integration of the LiteLLM library, facilitating multi-model support and unified API access.
- **LocalAI**: Integration with the LocalAI ecosystem, allowing for local model execution and management.

## Core Features

- **VS Code & Copilot Integration**: Native support for extending the VS Code experience and enhancing Copilot's capabilities.
- **MCP Server Support**: Built-in support for Model Context Protocol (MCP) servers, including specialized tools for terminal interaction and debugging.
- **Multi-Model Orchestration**: Leverages LiteLLM to provide a unified interface for various LLM providers.
- **Local Execution**: Seamlessly integrates with LocalAI for private and local AI processing.

## Repository Structure

- `src/Esi.AI.LiteLlm`: C# implementation of LiteLLM.
- `src/Esi.AI.LocalAI`: Integration with LocalAI.
- `src/vscode/vscode-esi-mcp`: VS Code extension and MCP server components.

