/** Демо-токен: mock.demo:<id> или JWT с claim demo=true (без проверки подписи). */
export function tokenLooksDemo(token: string | null | undefined): boolean {
  if (!token) return false
  if (token.startsWith('mock.demo:')) return true
  const parts = token.split('.')
  if (parts.length < 2) return false
  try {
    const json = parts[1].replace(/-/g, '+').replace(/_/g, '/')
    const padded = json + '='.repeat((4 - (json.length % 4)) % 4)
    const payload = JSON.parse(atob(padded)) as { demo?: unknown }
    return payload.demo === true || payload.demo === 'true'
  } catch {
    return false
  }
}
