# Backend-aware configuration profiles

The shared configuration profile contract now includes a `ConfigurationBackend` enum with `Llama` and `OpenVino` values. Existing profiles remain compatible because the persisted database column defaults to `Llama`.

OpenVINO settings are represented by `OpenVinoSettings` and serialized into the profile `ConfigurationJson` alongside the model path, selected device route, device enablement and weights, cache directory, and generation parameters. The Backends page now uses the same create, update, assign, model-path matching, and profile restoration flow for OpenVINO as for LLama. The legacy LLama page filters out OpenVINO profiles.

Validation completed:
- [done] Added the shared backend enum and OpenVINO settings contract.
- [done] Added the EF Core backend column migration and model snapshot update.
- [done] Added backend-aware OpenVINO profile UI and serialization.
- [done] Preserved legacy LLama profile behavior and filtering.
- [done] Studio build passed with `dotnet build src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj --no-restore`.
- [done] Added the LLama-style OpenVINO model dropdown backed by the existing current model list, retaining a custom path fallback.
# Request
Remove the horizontal scrollbar from the Backends page.

# Action plan
- [done] Inspect the shared layout and Backends page styles for horizontal overflow.
- [done] Apply the smallest CSS/layout fix that keeps content within the viewport.
- [done] Validate the change with a focused build and browser check.

# Summary
The shared desktop layout allowed its flex child `main` to retain an automatic minimum width. Setting `main { min-width: 0; }` lets the page content shrink to the available viewport width. The validation card was then reduced from `24rem` to `12rem` on desktop, with the existing mobile full-width rule retained. GPU routing now defaults newly detected Vulkan and OpenVINO devices to disabled, preserves explicit existing user settings, and displays hardware descriptions before technical identifiers. OpenVINO diagnostics hide the verbose `SUPPORTED_PROPERTIES` value and use `FULL_DEVICE_NAME` for display. Live validation showed OpenVINO returning `GPU.0` Intel UHD 630, `GPU.1` Intel Graphics `[0xe223]` (the B70), and `GPU.2` NVIDIA GeForce RTX 4070; no NPU device was returned. The Studio project build completed successfully without new warnings.

# Current Request

Reconcile the OpenVINO implementation with the official `openvino.genai` repository and the OpenVINO 2025 GenAI documentation. Direct `.gguf` file loading is supported for selected architectures; OpenVINO IR models remain directory-based.

# Action plan


# Final Summary

The OpenVINO loader now accepts either an existing `.gguf` file for supported direct GenAI loading or an existing OpenVINO IR/GenAI model directory. The exact normalized path is retained in runtime status and profiles. The shared load request carries `CACHE_DIR`, `MaxNewTokens`, `Temperature`, `TopP`, and `DoSample`; the loader applies the latter values through the local wrapper's `GenerationConfig` setters. The OpenVINO model picker now refreshes the actual local GGUF inventory and labels direct GGUF versus IR directory input.

 # Current Request

 Make OpenVINO driver validation collapsible and collapse it automatically when the accelerator is ready.

 # Action plan

 - [done] Convert the driver validation section to a native collapsible details panel.
 - [done] Keep the validation status visible while collapsed and preserve manual recheck access when expanded.
 - [done] Automatically collapse the panel when GPU or NPU readiness is confirmed.
 - [done] Add the OpenVINO loaded-model state required by the LLama-aligned layout.
 - [done] Validate the client and complete Studio builds.

 # Final Summary

 The OpenVINO driver validation area is now a native `<details>` panel. It stays open while diagnostics are missing or no compatible accelerator is ready, and renders collapsed once the GPU or NPU is ready. The collapsed summary shows `Ready` or `Needs attention`, while the existing recheck and troubleshooting controls remain available inside the expanded panel. The OpenVINO loaded-model state used by the surrounding layout is now declared and updated from the load result. Both `dotnet build src/Esi.AI/Esi.AI.Studio.Client/Esi.AI.Studio.Client.csproj --no-restore` and `dotnet build src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj --no-restore` passed.
The official OpenVINO 2026.3 documentation confirms direct GGUF loading remains architecture-dependent and in preview. `SchedulerConfig`/KV-cache controls are separate from `GenerationConfig` and are not exposed by the current C# wrapper, so they were intentionally not represented as unsupported string properties. `dotnet build src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj --no-restore` passed and editor diagnostics reported no errors. `dotnet test src/Esi.AI.sln --no-restore` could not run because the checked-in solution contains incorrect `src/src/...` project paths; there is no test project under `src`.

The official OpenVINO 2026.3 NPU guide is now reflected as well. The wrapper accepts five pipeline property pairs so Esi.AI can pass `CACHE_DIR`, `MAX_PROMPT_LEN`, `MIN_RESPONSE_LEN`, `PREFILL_HINT`, and `GENERATE_HINT` together. NPU detection is exposed separately from GPU readiness, the loader accepts the `NPU` route, and the Backends page persists and displays the NPU context/performance settings. GPU and NPU routes are kept mutually exclusive. The NPU documentation's exported-IR examples and architecture-dependent GGUF caveat remain explicit.

The OpenVINO load handler now uses the same GPU-or-NPU readiness condition as the load button. The local model scanner finds `.gguf` files recursively in the configured model directories and passes their absolute paths to the server. No GGUF file is currently present in this workspace, so native loading against a real model remains the final environment-dependent check; the Studio build and editor diagnostics are clean.

# Current Request

After a Hugging Face download completed, the Models page still showed the old local model count until the user manually refreshed the library.

# Action plan

- [done] Reproduce the stale local list in the browser after download completion.
- [done] Confirm the downloaded GGUF file exists in the configured model directory.
- [done] Run the completion scan and UI refresh on the Blazor synchronization context.
- [done] Build the Studio client and inspect Razor diagnostics.

# Final Summary

The download completion path in `Models.razor` now invokes the local model scan and `StateHasChanged` together through `InvokeAsync`. The completed `SmolLM2-135M-BF16.gguf` was confirmed on disk, and manual refresh confirmed the scanner discovers it as the ninth local GGUF model. The focused client build passed and the edited Razor file has no diagnostics.

# Current Request

Replace download polling with a SignalR push update so clients receive the download state and refreshed local model list when a download completes.

# Action plan

- [done] Confirm the old client-side polling loop and existing SignalR connection.
- [done] Add the shared `ModelDownloadUpdate` contract.
- [done] Push download progress and completion updates through `DataHub` clients.
- [done] Include the freshly scanned local model list in the completion update.
- [done] Subscribe `Models.razor` to the SignalR event and remove polling.
- [done] Build the complete Studio project and inspect all changed-file diagnostics.

# Final Summary

Downloads now use a push-based SignalR flow. `ModelLibraryService` publishes `ModelDownloadUpdated` through `IHubContext<DataHub>` for initial, progress, completed, and failed states. The completed message contains the refreshed local GGUF inventory. `SignalRDataService` exposes the event to the Blazor client, while `Models.razor` updates its transfer list and local model list from that event and unsubscribes on disposal. The old one-second polling loop was removed. `dotnet build src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj --no-restore` passed, and all changed source files report no diagnostics.

# Current Request

Make OpenVINO model selection behave like LLama selection through configuration sets.

# Action plan

- [done] Compare LLama and OpenVINO profile selection and initialization paths.
- [done] Identify that OpenVINO profiles were displayed but not loaded when switching tabs.
- [done] Load the matching or default OpenVINO profile when entering the OpenVINO tab.
- [done] Validate the client and complete Studio builds plus Razor diagnostics.

# Final Summary

OpenVINO now follows the same configuration-set flow as LLama when the backend tab is selected. The matching profile for the current model is retained; when no model is selected, the default or most recently updated OpenVINO profile is loaded into `openVinoForm`, including its model path, device route, device settings, cache directory, generation options, and NPU settings. Existing profile selection, create, update, and assign operations remain shared. The client build and complete Studio build both passed, and the edited Razor component reports no diagnostics.

# Current Request

Rename configuration profiles from LLama-specific names to model-wide names.

# Action plan

- [done] Rename the shared profile contract and client service methods to `ModelConfigurationProfile`.
- [done] Rename the SignalR hub methods and server persistence entity to model-wide names.
- [done] Preserve the existing database table mapping for stored profiles.
- [done] Allow equal profile names across different backends while retaining duplicate protection within one backend.
- [done] Validate the client and complete Studio builds, diagnostics, and live OpenVINO profile loading.

# Final Summary

Configuration profiles now use `ModelConfigurationProfile`, `ModelConfigurationProfiles`, and model-wide Get/Save/Delete/Default service and SignalR names. The existing `LlamaConfigurationProfiles` SQLite table remains mapped for data compatibility. Profile names may be reused between LLama and OpenVINO, and save/update failures are shown as status messages instead of unhandled Blazor exceptions. The live browser test successfully loaded both existing profiles, with `Variante1` selected for OpenVINO. The complete Studio build passed and all edited files report no diagnostics.

# Current Request

Complete the shared model-specific last-selected configuration profile flow for LLama and OpenVINO.

# Action plan

- [done] Centralize backend-specific profile JSON application in `TryApplyProfile`.
- [done] Restore the stored profile for the selected model before default/last-updated fallback.
- [done] Apply the same selection path when switching backend tabs and selecting either backend's model.
- [done] Register and apply the migration that adds `LlamaModels.ConfigurationProfileId`.
- [done] Validate client and Studio builds, editor diagnostics, migration application, and browser backend switching.

# Final Summary

LLama and OpenVINO now share one model/profile selection flow while retaining backend-specific settings in `ConfigurationJson`. The last selected profile is stored on the corresponding `LlamaModels` row and restored first; when no model is selected, the active backend falls back to its default or most recently updated profile. The manually added EF migration now has the required registration attributes, was applied successfully to the existing SQLite database, and the Backends page loaded successfully in the browser. Client and Studio builds passed, and all touched source files report no diagnostics.

# Current Request

Fix the internal server error on the Models page.

# Action plan

- [done] Identify the missing `IModelDownloadEvents` service during server-side prerendering.
- [done] Add a server-side no-op event adapter while retaining the client SignalR implementation.
- [done] Validate the Studio build, editor diagnostics, and the `/models` browser route.

# Final Summary

The Models page no longer fails during server-side prerendering. The server now provides a no-op `IModelDownloadEvents` adapter, while the WebAssembly client continues using `SignalRDataService` for pushed download updates. The Studio build and editor diagnostics passed, and `/models` loads successfully with the local model list.

# Current Request

Repair the chat workflow after `AddChatExchange` returned an empty model answer.

# Action plan

- [done] Reproduce the server-side chat failure with a loaded SmolLM2 GGUF model.
- [done] Confirm the default LLamaSharp history format was incompatible with SmolLM2 ChatML.
- [done] Add ChatML history formatting, stop-marker cleanup, and a finite generation limit.
- [done] Validate the complete chat flow, persisted messages, focused build, diagnostics, and final server run.

# Final Summary

LLama chat sessions now format system, user, and assistant messages using SmolLM-compatible ChatML markers and stop on `<|im_end|>`. Generation is capped at 128 new tokens so a missing stop marker cannot leave the SignalR call running indefinitely. The browser test successfully created a new conversation, persisted both messages, and displayed the assistant response with token statistics. The Studio build passed without errors.

# Current Request

Register all repositories from `.gitmodules` as clean, initialized Git submodules.

# Action plan

- [done] Initialize the Litellm and LocalAI submodules.
- [done] Convert OpenVINO from the tracked directory tree to the configured `csharp3.3` submodule.
- [done] Register LLamaSharp in the local submodule configuration.
- [done] Validate all four submodule worktrees and Gitlinks.

# Final Summary

All four paths are now initialized Git submodules: Litellm, LocalAI, LLamaSharp, and OpenVINO-CSharp-API. OpenVINO is checked out from `csharp3.3`. The previous OpenVINO directory was preserved temporarily at `/tmp/tmp.F2twZBzadn/OpenVINO-CSharp-API-existing`; it differed from the current remote branch in several source files and contained a local NuGet package.
