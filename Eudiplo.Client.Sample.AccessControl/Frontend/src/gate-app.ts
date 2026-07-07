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
  private _sessionId: string | null = null
  private _pollTimer: number | null = null
  private _stopped = false
  private readonly _onVisibilityChange = () => this._handleVisibilityChange()

  override connectedCallback() {
    super.connectedCallback()
    // Mobile browsers routinely suspend a backgrounded tab's network connections —
    // switching away to the wallet app to complete the scan is exactly that. The
    // EventSource can die silently while hidden, with no onerror ever firing (the page's
    // JS may be frozen, not just the connection). Re-subscribing on return is the fix —
    // EUDIPLO's SSE endpoint re-emits the session's *current* status immediately on
    // (re)connect, so this also self-heals if the session already completed while we
    // weren't looking. (Entryix's own kiosk flow hit the same class of issue.)
    document.addEventListener('visibilitychange', this._onVisibilityChange)
  }

  override disconnectedCallback() {
    super.disconnectedCallback()
    this._stopped = true
    document.removeEventListener('visibilitychange', this._onVisibilityChange)
    this._closeStream()
    this._clearPollTimer()
  }

  private _handleVisibilityChange() {
    // Also retry from 'error' — onerror may already have fired (and given up) while
    // backgrounded, before this handler got a chance to preempt it with a fresh subscribe.
    if (document.visibilityState === 'visible' && this._sessionId && (this._state === 'waiting' || this._state === 'error')) {
      this._state = 'waiting'
      this._subscribe(this._sessionId)
    }
  }

  private _closeStream() {
    this._eventSource?.close()
    this._eventSource = null
  }

  private _clearPollTimer() {
    if (this._pollTimer !== null) {
      clearTimeout(this._pollTimer)
      this._pollTimer = null
    }
  }

  private async _openGate() {
    this._state = 'opening'
    this._reason = null
    try {
      const res = await fetch('/api/gate/sessions', { method: 'POST' })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const { sessionId, requestUrl } = (await res.json()) as { sessionId: string; requestUrl: string }
      this._requestUrl = requestUrl
      this._sessionId = sessionId
      this._state = 'waiting'
      this._subscribe(sessionId)
      this._schedulePoll(sessionId)
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
      this._applyStatus(data)
    }

    source.onerror = () => {
      // A dropped connection mid-wait isn't a verdict — surface it distinctly rather than
      // silently reading as denied. The poll fallback (see _schedulePoll) keeps checking
      // regardless, so this doesn't have to be the last word either.
      if (this._state === 'waiting') {
        this._state = 'error'
        this._reason = 'Lost connection to the gate backend.'
      }
      this._closeStream()
    }
  }

  /// Belt-and-suspenders alongside the SSE subscription above: a real device backgrounds
  /// the tab for the entire "unlock wallet, pick credential, confirm" interaction, and
  /// mobile browsers can silently kill a backgrounded tab's EventSource without ever
  /// firing `onerror` — the visibilitychange-triggered resubscribe (see
  /// _handleVisibilityChange) covers most of that, but isn't observed to be fully reliable
  /// in practice. This plain poll doesn't depend on any live connection surviving
  /// backgrounding at all — only on timers resuming when the tab is foregrounded again,
  /// which is far more consistently supported.
  private _schedulePoll(sessionId: string) {
    this._pollTimer = window.setTimeout(async () => {
      if (this._stopped || this._sessionId !== sessionId) return
      try {
        const res = await fetch(`/api/gate/sessions/${sessionId}`)
        if (res.ok) this._applyStatus((await res.json()) as SessionEvent)
      } catch {
        // Transient — the next poll tick or the SSE subscription may still come through.
      }
      if (!this._stopped && this._sessionId === sessionId) this._schedulePoll(sessionId)
    }, 3000)
  }

  private _applyStatus(data: SessionEvent) {
    switch (data.status) {
      case 'completed':
        this._state = 'granted'
        this._closeStream()
        this._clearPollTimer()
        break
      case 'failed':
        this._state = 'denied'
        this._reason = data.errorReason ?? null
        this._closeStream()
        this._clearPollTimer()
        break
      case 'expired':
        this._state = 'expired'
        this._closeStream()
        this._clearPollTimer()
        break
      default:
        // active / fetched — still waiting on the wallet, nothing to render differently.
        break
    }
  }

  private _reset() {
    this._closeStream()
    this._clearPollTimer()
    this._state = 'idle'
    this._requestUrl = null
    this._reason = null
    this._sessionId = null
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
