import { LitElement, html, nothing } from 'lit'
import { customElement, state } from 'lit/decorators.js'
import { exploreTenant, type ExploreCredentials } from './api'
import type { ExploreResponse } from './types'
import './explorer-form'
import './explorer-results'

/** Top-level orchestrator: owns the request state, delegates form and results to children. */
@customElement('explorer-app')
export class ExplorerApp extends LitElement {
  override createRenderRoot() {
    return this
  }

  @state() private _loading = false
  @state() private _error: string | null = null
  @state() private _result: ExploreResponse | null = null

  private async _handleExplore(e: CustomEvent<ExploreCredentials>) {
    this._loading = true
    this._error = null
    this._result = null
    try {
      this._result = await exploreTenant(e.detail)
    } catch (err) {
      this._error = err instanceof Error ? err.message : String(err)
    } finally {
      this._loading = false
    }
  }

  override render() {
    return html`
      <header>
        <p class="eyebrow">Eudiplo.Client sample</p>
        <h1>Explorer</h1>
        <p class="sub">
          Point this at any EUDIPLO tenant and see what's reachable with its credentials.
          Nothing is stored — sent once, used for this request, then discarded.
        </p>
      </header>

      <explorer-form
        .loading=${this._loading}
        @explore=${(e: CustomEvent<ExploreCredentials>) => this._handleExplore(e)}
      ></explorer-form>

      ${this._error ? html`<p class="error top-error">${this._error}</p>` : nothing}
      ${this._result ? html`<explorer-results .result=${this._result}></explorer-results>` : nothing}
    `
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'explorer-app': ExplorerApp
  }
}
