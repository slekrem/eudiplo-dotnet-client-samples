import { LitElement, html } from 'lit'
import { customElement, property } from 'lit/decorators.js'
import type { ExploreResponse } from './types'
import './explorer-section'

/** The grid of result cards — one `<explorer-section>` per key in the response. */
@customElement('explorer-results')
export class ExplorerResults extends LitElement {
  override createRenderRoot() {
    return this
  }

  @property({ attribute: false }) result!: ExploreResponse

  override render() {
    return html`
      <div class="grid">
        ${Object.entries(this.result).map(
          ([key, section]) => html`<explorer-section name=${key} .result=${section}></explorer-section>`,
        )}
      </div>
    `
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'explorer-results': ExplorerResults
  }
}
