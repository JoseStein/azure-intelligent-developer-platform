import { PublicClientApplication } from '@azure/msal-browser'

function required(name: string): string {
  const value = import.meta.env[name]
  if (!value) throw new Error(`Missing ${name}. Copy .env.example to .env.local and restart Vite.`)
  return value
}

export const apiUrl = required('VITE_API_URL')
export const apiScopes = [required('VITE_API_SCOPE')]
const redirectUri = required('VITE_ENTRA_REDIRECT_URI')
export const msalInstance = new PublicClientApplication({
  auth: {
    clientId: required('VITE_ENTRA_CLIENT_ID'),
    authority: `https://login.microsoftonline.com/${required('VITE_ENTRA_TENANT_ID')}`,
    redirectUri,
    postLogoutRedirectUri: new URL('/', redirectUri).toString(),
  },
  cache: { cacheLocation: 'sessionStorage' },
})
