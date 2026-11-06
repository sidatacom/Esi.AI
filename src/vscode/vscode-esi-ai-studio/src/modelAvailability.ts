interface ModelAvailability {
  loaded?: unknown;
  autoLaunch?: unknown;
}

export function isModelAvailable(model: ModelAvailability): boolean {
  return model.loaded === true || model.autoLaunch === true;
}
