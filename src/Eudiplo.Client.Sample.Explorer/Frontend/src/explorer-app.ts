import { LitElement, html, nothing } from 'lit'
import { customElement, state } from 'lit/decorators.js'

interface QueryResult {
  ok: boolean
  data?: unknown
  error?: string | null
}

type ExploreResponse = Record<string, QueryResult>

@customElement('explorer-app')
export class ExplorerApp extends LitElement {
  // No Shadow DOM — uses the global stylesheet (src/styles/global.css) instead of
  // component-scoped styles.
  override createRenderRoot() {
    return this
  }

  @state() private _baseUrl = ''
  @state() private _clientId = ''
  @state() private _clientSecret = ''

  @state() private _loading = false
  @state() private _error: string | null = null
  @state() private _result: ExploreResponse | null = null

  private async _explore(e: Event) {
    e.preventDefault()
    this._loading = true
    this._error = null
    this._result = null
    try {
      const res = await fetch('/api/explore', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          baseUrl: this._baseUrl,
          clientId: this._clientId,
          clientSecret: this._clientSecret,
        }),
      })
      const body = (await res.json()) as ExploreResponse & { error?: string }
      if (!res.ok) throw new Error(body?.error ?? `HTTP ${res.status}`)
      this._result = body
    } catch (err) {
      // A failed fetch (network error, unreachable EUDIPLO instance, bad JSON, or a 4xx
      // from the backend's own validation) — distinct from a single section failing below,
      // which the backend already isolates per query.
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

      <form class="card" @submit=${(e: Event) => this._explore(e)}>
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
        <button ?disabled=${this._loading}>${this._loading ? 'Exploring…' : 'Explore'}</button>
      </form>

      ${this._error ? html`<p class="error top-error">${this._error}</p>` : nothing}
      ${this._result ? this._renderResult(this._result) : nothing}
    `
  }

  private _renderResult(result: ExploreResponse) {
    return html`
      <div class="grid">
        ${Object.entries(result).map(([key, section]) => this._renderSection(key, section))}
      </div>
    `
  }

  private _renderSection(key: string, section: QueryResult) {
    return html`
      <section class="card section">
        <h2>${humanize(key)}</h2>
        ${section.ok ? this._renderData(section.data) : html`<p class="error">${section.error}</p>`}
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

// "keyChains" -> "Key Chains", "verifierConfigs" -> "Verifier Configs" — the section keys
// are the backend's own camelCase property names, so titles stay in sync automatically as
// queries are added or renamed there.
function humanize(key: string): string {
  return key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase())
}

declare global {
  interface HTMLElementTagNameMap {
    'explorer-app': ExplorerApp
  }
}
