import type { ExploreResponse } from './types'

export interface ExploreCredentials {
  baseUrl: string
  clientId: string
  clientSecret: string
}

// A failed fetch (network error, unreachable EUDIPLO instance, bad JSON, or a 4xx from the
// backend's own validation) throws here — distinct from a single section failing, which
// the backend already isolates per query (see ExploreResponse's per-key QueryResult).
export async function exploreTenant(credentials: ExploreCredentials): Promise<ExploreResponse> {
  const res = await fetch('/api/explore', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(credentials),
  })
  const body = (await res.json()) as ExploreResponse & { error?: string }
  if (!res.ok) throw new Error(body?.error ?? `HTTP ${res.status}`)
  return body
}
