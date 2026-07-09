import { LitElement, html } from 'lit'
import { customElement, property } from 'lit/decorators.js'
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

  override render() {
    return html`
      <section class="card section">
        <h2>${humanize(this.name)}</h2>
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
        <pre class="json">${JSON.stringify(data, null, 2)}</pre>
      `
    }
    if (typeof data === 'string') return html`<p class="value">${data}</p>`
    return html`<pre class="json">${JSON.stringify(data, null, 2)}</pre>`
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'explorer-section': ExplorerSection
  }
}
