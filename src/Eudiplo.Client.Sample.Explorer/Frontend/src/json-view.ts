import { html } from 'lit'
import { humanize } from './format'

// A generic "labeled fields" renderer for arbitrary EUDIPLO data — no per-resource-type
// layout code. An object becomes a <dl> of humanized-label / value rows; an array of
// objects becomes one such block per item; primitives render inline. Recurses for nested
// objects/arrays, so a key-chain's nested activePublicKey/rotationPolicy show up as
// indented sub-fields instead of an opaque blob.
export function renderJsonValue(value: unknown): unknown {
  if (value === null || value === undefined) {
    return html`<span class="jv-null">—</span>`
  }

  if (Array.isArray(value)) {
    if (value.length === 0) return html`<span class="jv-null">Empty</span>`
    const allPrimitive = value.every((v) => v === null || typeof v !== 'object')
    if (allPrimitive) {
      return html`<span class="jv-value">${value.map((v) => primitiveText(v)).join(', ')}</span>`
    }
    return html`
      <div class="jv-list">${value.map((item) => html`<div class="jv-item">${renderJsonValue(item)}</div>`)}</div>
    `
  }

  if (typeof value === 'object') {
    const entries = Object.entries(value as Record<string, unknown>)
    if (entries.length === 0) return html`<span class="jv-null">Empty</span>`
    return html`
      <dl class="jv-fields">
        ${entries.map(([key, v]) => html`<dt>${humanize(key)}</dt><dd>${renderJsonValue(v)}</dd>`)}
      </dl>
    `
  }

  return renderPrimitive(value)
}

function primitiveText(value: unknown): string {
  return value === null ? 'null' : String(value)
}

function renderPrimitive(value: unknown) {
  if (typeof value === 'boolean') {
    return html`<span class="jv-bool">${value ? 'true' : 'false'}</span>`
  }
  const text = String(value)
  // Certificates, JWTs, and base64 keys are long and often multi-line — inline text would
  // blow out the field grid, so those get a small scrollable box instead. Everything else
  // (ids, dates, short labels) reads fine as plain text next to its own field label.
  if (text.length > 100 || text.includes('\n')) {
    return html`<pre class="jv-blob">${text}</pre>`
  }
  return html`<span class="jv-value">${text}</span>`
}
