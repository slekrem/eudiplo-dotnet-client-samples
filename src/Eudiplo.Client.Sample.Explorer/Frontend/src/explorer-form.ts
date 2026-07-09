import { LitElement, html, nothing } from 'lit'
import { customElement, property, state } from 'lit/decorators.js'
import type { ExploreCredentials } from './api'

/** Emits an `explore` CustomEvent<ExploreCredentials> on submit — never calls the API itself. */
@customElement('explorer-form')
export class ExplorerForm extends LitElement {
  // No Shadow DOM — uses the global stylesheet (src/styles/global.css) instead of
  // component-scoped styles.
  override createRenderRoot() {
    return this
  }

  @property({ type: Boolean }) loading = false

  @state() private _baseUrl = ''
  @state() private _clientId = ''
  @state() private _clientSecret = ''

  private _submit(e: Event) {
    e.preventDefault()
    this.dispatchEvent(
      new CustomEvent<ExploreCredentials>('explore', {
        detail: { baseUrl: this._baseUrl, clientId: this._clientId, clientSecret: this._clientSecret },
        bubbles: true,
        composed: true,
      }),
    )
  }

  override render() {
    return html`
      <form class="card" @submit=${(e: Event) => this._submit(e)}>
        <label>
          EUDIPLO base URL
          <input
            type="url"
            placeholder="https://your-eudiplo-instance.example"
            .value=${this._baseUrl}
            @input=${(e: Event) => (this._baseUrl = (e.target as HTMLInputElement).value)}
            required
          />
        </label>
        <label>
          Client ID
          <input
            type="text"
            autocomplete="off"
            .value=${this._clientId}
            @input=${(e: Event) => (this._clientId = (e.target as HTMLInputElement).value)}
            required
          />
        </label>
        <label>
          Client secret
          <input
            type="password"
            autocomplete="off"
            .value=${this._clientSecret}
            @input=${(e: Event) => (this._clientSecret = (e.target as HTMLInputElement).value)}
            required
          />
        </label>
        <button ?disabled=${this.loading}>
          ${this.loading ? html`<span class="spinner"></span>` : nothing} ${this.loading ? 'Exploring…' : 'Explore'}
        </button>
      </form>
    `
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'explorer-form': ExplorerForm
  }
}
