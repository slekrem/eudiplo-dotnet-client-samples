import { LitElement, html } from 'lit'
import { customElement, property, state } from 'lit/decorators.js'
import type { QueryResult } from './types'
import { humanize } from './format'

/** One card in the results grid — a single query's outcome (ok/data, or error). */
@customElement('explorer-section')
export class ExplorerSection extends LitElement {
  override createRenderRoot() {
    return this
  }

  @property() name = ''
  @property({ attribute: false }) result!: QueryResult

  @state() private _copied = false

  override render() {
    const status = this.result.ok ? 'is-ok' : 'is-error'
    return html`
      <section class="card section ${status}">
        <div class="heading">
          <span class="status-dot ${status}"></span>
          <h2>${humanize(this.name)}</h2>
        </div>
        ${this.result.ok ? this._renderData(this.result.data) : html`<p class="error">${this.result.error}</p>`}
      </section>
    `
  }

  private _renderData(data: unknown) {
    if (data === null || data === undefined) {
      return html`<p class="muted">Not reported by this instance.</p>`
    }
    if (Array.isArray(data)) {
      if (data.length === 0) return html`<p class="muted">Empty.</p>`
      return html`
        <p class="count">${data.length} item${data.length === 1 ? '' : 's'}</p>
        ${this._renderJson(data)}
      `
    }
    if (typeof data === 'string') return html`<p class="value">${data}</p>`
    return this._renderJson(data)
  }

  private _renderJson(data: unknown) {
    const text = JSON.stringify(data, null, 2)
    return html`
      <div class="json-wrap">
        <button class="copy-btn ${this._copied ? 'is-copied' : ''}" type="button" @click=${() => this._copy(text)}>
          ${this._copied ? 'Copied' : 'Copy'}
        </button>
        <pre class="json">${text}</pre>
      </div>
    `
  }

  private async _copy(text: string) {
    try {
      await navigator.clipboard.writeText(text)
      this._copied = true
      setTimeout(() => (this._copied = false), 1500)
    } catch {
      // Clipboard API can be unavailable (insecure context, denied permission) — the
      // button just silently stays "Copy" rather than pretending it worked.
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'explorer-section': ExplorerSection
  }
}
