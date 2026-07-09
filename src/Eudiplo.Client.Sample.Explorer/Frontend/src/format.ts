// "keyChains" -> "Key Chains", "verifierConfigs" -> "Verifier Configs" — the section keys
// are the backend's own camelCase property names, so titles stay in sync automatically as
// queries are added or renamed there.
export function humanize(key: string): string {
  return key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase())
}
