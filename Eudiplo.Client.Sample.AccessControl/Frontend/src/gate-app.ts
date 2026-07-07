import { LitElement, html, nothing } from 'lit'
import { customElement, state } from 'lit/decorators.js'
import QRCode from 'qrcode'

type GateState = 'idle' | 'opening' | 'waiting' | 'granted' | 'denied' | 'expired' | 'error'

interface SessionEvent {
  status?: string
  errorReason?: string
}

@customElement('gate-app')
export class GateApp extends LitElement {
  // No Shadow DOM — uses the global stylesheet (src/styles/global.css) instead of
  // component-scoped styles.
  override createRenderRoot() {
    return this
  }

  @state() private _state: GateState = 'idle'
  @state() private _requestUrl: string | null = null
  @state() private _reason: string | null = null

  private _eventSource: EventSource | null = null

  override disconnectedCallback() {
    super.disconnectedCallback()
    this._closeStream()
  }

  private _closeStream() {
    this._eventSource?.close()
    this._eventSource = null
  }

  private async _openGate() {
    this._state = 'opening'
    this._reason = null
    try {
      const res = await fetch('/api/gate/sessions', { method: 'POST' })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const { sessionId, requestUrl } = (await res.json()) as { sessionId: string; requestUrl: string }
      this._requestUrl = requestUrl
      this._state = 'waiting'
      this._subscribe(sessionId)
    } catch (err) {
      this._state = 'error'
      this._reason = err instanceof Error ? err.message : String(err)
    }
  }

  private _subscribe(sessionId: string) {
    this._closeStream()
    const source = new EventSource(`/api/gate/sessions/${sessionId}/events`)
    this._eventSource = source

    source.onmessage = (ev) => {
      let data: SessionEvent
      try {
        data = JSON.parse(ev.data) as SessionEvent
      } catch {
        return
      }
      switch (data.status) {
        case 'completed':
          this._state = 'granted'
          this._closeStream()
          break
        case 'failed':
          this._state = 'denied'
          this._reason = data.errorReason ?? null
          this._closeStream()
          break
        case 'expired':
          this._state = 'expired'
          this._closeStream()
          break
        default:
          // active / fetched — still waiting on the wallet, nothing to render differently.
          break
      }
    }

    source.onerror = () => {
      // A dropped connection mid-wait isn't a verdict — surface it distinctly rather than
      // silently reading as denied.
      if (this._state === 'waiting') {
        this._state = 'error'
        this._reason = 'Lost connection to the gate backend.'
      }
      this._closeStream()
    }
  }

  private _reset() {
    this._closeStream()
    this._state = 'idle'
    this._requestUrl = null
    this._reason = null
  }

  override updated(changed: Map<string, unknown>) {
    if (changed.has('_state') && this._state === 'waiting' && this._requestUrl) {
      const canvas = this.querySelector<HTMLCanvasElement>('#qr-canvas')
      if (canvas) {
        void QRCode.toCanvas(canvas, this._requestUrl, { width: 220, margin: 1 })
      }
    }
  }

  override render() {
    return html`
      <div class="card">
        <p class="eyebrow">Access Control</p>
        <h1>Gate</h1>
        ${this._renderBody()}
      </div>
    `
  }

  private _renderBody() {
    switch (this._state) {
      case 'idle':
        return html`
          <p>Scan an EUDI Wallet credential to open the gate.</p>
          <button @click=${() => this._openGate()}>Open Gate</button>
        `
      case 'opening':
        return html`<p>Opening gate…</p>`
      case 'waiting':
        return html`
          <div class="qr-wrap"><canvas id="qr-canvas"></canvas></div>
          <span class="badge waiting"><span class="spinner"></span> Waiting for wallet</span>
          <div class="link">${this._requestUrl}</div>
          <button class="secondary" @click=${() => this._reset()}>Cancel</button>
        `
      case 'granted':
        return html`
          <span class="badge granted">✓ Access granted</span>
          <button @click=${() => this._reset()}>Done</button>
        `
      case 'denied':
        return html`
          <span class="badge denied">✕ Access denied</span>
          ${this._reason ? html`<p>${this._reason}</p>` : nothing}
          <button @click=${() => this._reset()}>Try again</button>
        `
      case 'expired':
        return html`
          <span class="badge expired">⏱ Session expired</span>
          <button @click=${() => this._reset()}>Try again</button>
        `
      case 'error':
        return html`
          <span class="badge denied">⚠ Error</span>
          ${this._reason ? html`<p>${this._reason}</p>` : nothing}
          <button @click=${() => this._reset()}>Try again</button>
        `
      default:
        return nothing
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'gate-app': GateApp
  }
}
