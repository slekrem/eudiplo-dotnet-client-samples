// "keyChains" -> "Key Chains", "verifierConfigs" -> "Verifier Configs" — the section keys
// are the backend's own camelCase property names, so titles stay in sync automatically as
// queries are added or renamed there.
export function humanize(key: string): string {
  return key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase())
}

const JSON_TOKEN =
  /("(\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(?:true|false)\b|\bnull\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g

// A small, dependency-free JSON syntax highlighter — good enough for the read-only "look
// at this tenant's data" case this sample is for, not a general-purpose formatter. Escapes
// HTML-meaningful characters *before* wrapping tokens in spans, so the result is safe to
// render via Lit's unsafeHTML even though the JSON came from a remote server.
export function highlightJson(json: string): string {
  const escaped = json.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  return escaped.replace(JSON_TOKEN, (match) => {
    let cls = 'json-number'
    if (match.startsWith('"')) {
      cls = match.endsWith(':') ? 'json-key' : 'json-string'
    } else if (match === 'true' || match === 'false') {
      cls = 'json-boolean'
    } else if (match === 'null') {
      cls = 'json-null'
    }
    return `<span class="${cls}">${match}</span>`
  })
}
