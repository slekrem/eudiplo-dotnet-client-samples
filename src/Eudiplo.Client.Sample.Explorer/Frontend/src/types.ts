export interface QueryResult {
  ok: boolean
  data?: unknown
  error?: string | null
}

export type ExploreResponse = Record<string, QueryResult>
