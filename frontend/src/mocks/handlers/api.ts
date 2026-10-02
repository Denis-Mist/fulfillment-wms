export const api = (path: string) => `${import.meta.env.VITE_API_BASE_URL ?? '/api'}${path}`
