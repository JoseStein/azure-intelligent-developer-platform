import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { MsalProvider } from '@azure/msal-react'
import { msalInstance } from './auth'
import './styles.css'

await msalInstance.initialize()
const redirectResult = await msalInstance.handleRedirectPromise()
if (redirectResult?.account) msalInstance.setActiveAccount(redirectResult.account)

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <MsalProvider instance={msalInstance}>
      <App />
    </MsalProvider>
  </React.StrictMode>,
)
