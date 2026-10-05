import { useState, type FormEvent } from 'react'
import { useMsal } from '@azure/msal-react'
import { InteractionRequiredAuthError, InteractionStatus } from '@azure/msal-browser'
import { apiScopes, apiUrl } from './auth'

type ApplicationRequest = {
  pipelineRunId: number | null
  requestId: string
  resourceType: string
  applicationName: string
  runtime: string
  environment: string
  description: string | null
  status: string
  statusRefreshError?: string | null
  createdAt: string
}

type ValidationProblem = {
  errors?: Record<string, string[]>
  title?: string
}

type AiEvidence = {
  type: string
  statement: string
  sourceReference: string | null
}

type AiFinding = {
  category: string
  finding: string
  severity: string
  evidence: AiEvidence[]
  recommendation: string
}

type AiArchitectureRecommendation = {
  component: string
  purpose: string
  supportStatus: 'supported' | 'recommendationOnly'
  recommendation: string
}

type AiInfrastructureReview = {
  reviewId: string
  summary: string
  recommendedArchitecture: AiArchitectureRecommendation[]
  securityFindings: AiFinding[]
  reliabilityFindings: AiFinding[]
  costConsiderations: AiFinding[]
  missingInformation: AiFinding[]
  riskLevel: string
  confidence: { level: string; rationale: string }
  recommendedNextSteps: string[]
}

type TroubleshootingFinding = {
  finding: string
  evidence: AiEvidence[]
  confidence: { level: string; rationale: string }
  recommendedInvestigation: string
}

type DeploymentTroubleshootingResponse = {
  troubleshootingId: string
  suppliedEvidence: {
    pipelineName: string
    runId: number
    failedStage: string | null
    failedJob: string | null
    failedTask: string | null
    terraformCommandSupplied: boolean
    azureContextSupplied: boolean
    redactionsApplied: boolean
  }
  summary: string
  failureCategory: string
  findings: TroubleshootingFinding[]
  missingInformation: string[]
  recommendedNextSteps: string[]
  overallConfidence: { level: string; rationale: string }
}

type HealthMetricDraft = {
  id: number
  name: string
  aggregation: string
  value: string
  unit: string
  observedAt: string
}

type HealthEvidenceDraft = {
  id: number
  type: string
  observedAt: string
  title: string
  content: string
  metrics: HealthMetricDraft[]
}

type ApplicationHealthResponse = {
  analysisId: string
  suppliedEvidence: {
    applicationName: string
    environment: string
    windowStart: string | null
    windowEnd: string | null
    evidenceItemCount: number
    redactionsApplied: boolean
  }
  summary: string
  overallHealthAssessment: string
  findings: {
    finding: string
    category: string
    severity: string
    conclusionType: string
    evidence: { provenance: string; statement: string; sourceReference: string | null }[]
    confidence: { level: string; rationale: string }
    recommendedInvestigation: string
  }[]
  missingInformation: string[]
  recommendedNextSteps: string[]
  overallConfidence: { level: string; rationale: string }
}

type LiveHealthResponse = {
  collection: { status: string; requestedMetricCount: number; collectedMetricCount: number; durationMilliseconds: number }
  applicationInsightsCollection?: { status: string; queryCount: number; returnedSummaryCount: number; durationMilliseconds: number }
  analysis: ApplicationHealthResponse | null
}

const aiReviewUrl = apiUrl.replace(/\/api\/requests\/?$/, '/api/ai/review')
const troubleshootingUrl = apiUrl.replace(/\/api\/requests\/?$/, '/api/ai/troubleshoot')
const healthAnalysisUrl = apiUrl.replace(/\/api\/requests\/?$/, '/api/ai/health/analyze')
const liveHealthAnalysisUrl = apiUrl.replace(/\/api\/requests\/?$/, '/api/ai/health/analyze-live')
const displayValue = (value: string) => value.replace(/([A-Z])/g, ' $1').replace(/^./, letter => letter.toUpperCase())
const requiresInteractiveToken = (error: unknown) => {
  if (error instanceof InteractionRequiredAuthError) return true
  if (typeof error !== 'object' || error === null || !('errorCode' in error)) return false
  return ['interaction_required', 'login_required', 'consent_required']
    .includes(String(error.errorCode).toLowerCase())
}
const msalDiagnostic = (error: unknown) => {
  if (typeof error !== 'object' || error === null) return {}
  const value = error as { errorCode?: unknown; subError?: unknown }
  const result: Record<string, string> = {}
  if (typeof value.errorCode === 'string' && value.errorCode.length <= 80) result.msalErrorCode = value.errorCode
  if (typeof value.subError === 'string' && value.subError.length <= 80) result.msalSubError = value.subError
  return result
}
const canSupplyCurrentRequest = (resourceType: string, applicationName: string, runtime: string, environment: string, description: string) =>
  resourceType === 'appservice' &&
  applicationName.length >= 3 &&
  applicationName.length <= 30 &&
  applicationName !== 'aidp' &&
  /^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(applicationName) &&
  runtime === 'dotnet10' &&
  environment === 'dev' &&
  description.length <= 200

let healthDraftId = 0
const metricUnits: Record<string, string[]> = {
  requests: ['count'], http5xx: ['count', 'percent'], http4xx: ['count', 'percent'],
  averageResponseTime: ['ms', 's'], cpuPercentage: ['percent'],
  memoryWorkingSetBytes: ['bytes', 'kb', 'mb', 'gb'], dependencyFailures: ['count', 'percent'],
  availability: ['percent'],
}
const newHealthMetric = (): HealthMetricDraft => ({
  id: ++healthDraftId, name: 'requests', aggregation: 'count', value: '', unit: 'count', observedAt: '',
})
const newHealthEvidence = (): HealthEvidenceDraft => ({
  id: ++healthDraftId, type: 'appServiceLog', observedAt: '', title: '', content: '', metrics: [],
})
const toIsoOrNull = (value: string) => value ? new Date(value).toISOString() : null


function App() {
  const { instance, accounts, inProgress } = useMsal()
  const account = instance.getActiveAccount() ?? accounts[0]
  const authBusy = inProgress !== InteractionStatus.None

  async function signIn() {
    setErrors([])
    try {
      await instance.loginRedirect({ scopes: apiScopes })
    } catch {
      setErrors(['Sign-in could not start. Please try again.'])
    }
  }

  async function signOut() {
    setCreatedRequest(null)
    setTroubleshootingResult(null)
    setHealthResult(null)
    setRefreshMessage('')
    setErrors([])
    try {
      await instance.logoutRedirect({ account })
    } catch {
      setErrors(['Sign-out could not complete. Please try again.'])
    }
  }

  // Shared bearer-token path for POST and future GET request calls.
  async function apiFetch(url: string, options: RequestInit = {}) {
    if (!account) throw new Error('Sign in before submitting a request.')
    let accessToken: string
    try {
      const result = await instance.acquireTokenSilent({ account, scopes: apiScopes })
      accessToken = result.accessToken
    } catch (error) {
      if (requiresInteractiveToken(error)) {
        const diagnostic = msalDiagnostic(error)
        await instance.acquireTokenRedirect({ scopes: apiScopes, prompt: 'select_account' })
        throw new Error(`MSAL_INTERACTION_REQUIRED:${diagnostic.msalErrorCode ?? 'interaction_required'}:${diagnostic.msalSubError ?? 'none'}`)
      }
      const diagnostic = msalDiagnostic(error)
      throw new Error(`MSAL_TOKEN_ACQUISITION_FAILED:${diagnostic.msalErrorCode ?? 'unknown'}:${diagnostic.msalSubError ?? 'none'}`)
    }
    const headers = new Headers(options.headers)
    headers.set('Authorization', `Bearer ${accessToken}`)
    return fetch(url, { ...options, headers })
  }

  const [resourceType, setResourceType] = useState('appservice')
  const [applicationName, setApplicationName] = useState('')
  const [runtime, setRuntime] = useState('dotnet10')
  const [environment, setEnvironment] = useState('dev')
  const [description, setDescription] = useState('')
  const [createdRequest, setCreatedRequest] = useState<ApplicationRequest | null>(null)
  const [errors, setErrors] = useState<string[]>([])
  const [submitting, setSubmitting] = useState(false)
  const [refreshingStatus, setRefreshingStatus] = useState(false)
  const [refreshMessage, setRefreshMessage] = useState('')
  const [lookupRequestId, setLookupRequestId] = useState('')
  const [lookingUp, setLookingUp] = useState(false)
  const [lookupMessage, setLookupMessage] = useState('')
  const [reviewIntent, setReviewIntent] = useState('')
  const [aiReview, setAiReview] = useState<AiInfrastructureReview | null>(null)
  const [reviewing, setReviewing] = useState(false)
  const [reviewMessage, setReviewMessage] = useState('')
  const [troubleshootingForm, setTroubleshootingForm] = useState({
    pipelineName: '', runId: '', failedStage: '', failedJob: '', failedTask: '', errorText: '',
    terraformCommand: '', service: '', azureResourceType: '', azureResourceName: '', resourceGroup: '', region: '',
  })
  const [troubleshootingResult, setTroubleshootingResult] = useState<DeploymentTroubleshootingResponse | null>(null)
  const [troubleshootingMessage, setTroubleshootingMessage] = useState('')
  const [troubleshooting, setTroubleshooting] = useState(false)
  const [healthForm, setHealthForm] = useState({
    applicationName: '', environment: 'dev', question: '', azureService: 'appservice',
    resourceName: '', resourceGroup: '', region: '', windowStart: '', windowEnd: '',
  })
  const [healthEvidence, setHealthEvidence] = useState<HealthEvidenceDraft[]>([newHealthEvidence()])
  const [healthResult, setHealthResult] = useState<ApplicationHealthResponse | null>(null)
  const [healthMessage, setHealthMessage] = useState('')
  const [liveHealthDiagnostics, setLiveHealthDiagnostics] = useState<Record<string, string> | null>(null)
  const [analyzingHealth, setAnalyzingHealth] = useState(false)
  const [liveHealthResult, setLiveHealthResult] = useState<LiveHealthResponse | null>(null)
  const [analyzingLiveHealth, setAnalyzingLiveHealth] = useState(false)

  const updateTroubleshootingField = (field: keyof typeof troubleshootingForm, value: string) =>
    setTroubleshootingForm(current => ({ ...current, [field]: value }))

  const updateHealthField = (field: keyof typeof healthForm, value: string) =>
    setHealthForm(current => ({ ...current, [field]: value }))
  const updateHealthEvidence = (id: number, update: Partial<HealthEvidenceDraft>) =>
    setHealthEvidence(current => current.map(item => item.id === id ? { ...item, ...update } : item))
  const updateHealthMetric = (evidenceId: number, metricId: number, update: Partial<HealthMetricDraft>) =>
    setHealthEvidence(current => current.map(item => item.id === evidenceId
      ? { ...item, metrics: item.metrics.map(metric => metric.id === metricId ? { ...metric, ...update } : metric) }
      : item))

  async function analyzeHealth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!account || authBusy || analyzingHealth) return
    if (healthForm.applicationName.trim().length < 3 || !healthForm.environment.trim() || healthForm.question.trim().length < 20 ||
        healthEvidence.length === 0 || healthEvidence.some(item => !item.title.trim() || item.content.trim().length < 10)) {
      setHealthMessage('Enter the required target details, a question of at least 20 characters, and complete each evidence card.')
      return
    }
    if ((healthForm.windowStart && !healthForm.windowEnd) || (!healthForm.windowStart && healthForm.windowEnd)) {
      setHealthMessage('Supply both Window Start and Window End, or leave both blank.')
      return
    }
    const invalidMetric = healthEvidence.some(item => item.type === 'azureMetric' && item.metrics.some(metric =>
      metric.value.trim() === '' || !Number.isFinite(Number(metric.value))))
    if (invalidMetric) {
      setHealthMessage('Every metric row must include a finite numeric value.')
      return
    }

    setAnalyzingHealth(true)
    setHealthMessage('')
    setHealthResult(null)
    try {
      const response = await apiFetch(healthAnalysisUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          question: healthForm.question.trim(),
          target: {
            applicationName: healthForm.applicationName.trim(), environment: healthForm.environment.trim(),
            azureService: healthForm.azureService || null, resourceName: healthForm.resourceName.trim() || null,
            resourceGroup: healthForm.resourceGroup.trim() || null, region: healthForm.region.trim() || null,
          },
          windowStart: toIsoOrNull(healthForm.windowStart), windowEnd: toIsoOrNull(healthForm.windowEnd),
          evidence: healthEvidence.map(item => ({
            type: item.type, observedAt: toIsoOrNull(item.observedAt), title: item.title.trim(), content: item.content,
            metrics: item.type === 'azureMetric' ? item.metrics.map(metric => ({
              name: metric.name, aggregation: metric.aggregation, value: Number(metric.value),
              unit: metric.unit, observedAt: toIsoOrNull(metric.observedAt),
            })) : [],
          })),
        }),
      })
      if (!response.ok) {
        setHealthMessage(response.status === 400
          ? 'Check the health target, observation window, evidence, and metrics.'
          : response.status === 401 || response.status === 403
            ? 'You are not authorized to use application health analysis.'
            : 'Application health analysis is unavailable right now. Please try again later.')
        return
      }
      setHealthResult((await response.json()) as ApplicationHealthResponse)
      setHealthEvidence([newHealthEvidence()])
    } catch {
      setHealthMessage('Application health analysis is unavailable right now. Please try again later.')
    } finally {
      setAnalyzingHealth(false)
    }
  }

  async function analyzeLiveHealth() {
    if (!account || authBusy || analyzingLiveHealth || healthForm.question.trim().length < 20) {
      setHealthMessage('Enter a question of at least 20 characters before starting live analysis.')
      return
    }
    setAnalyzingLiveHealth(true)
    setHealthMessage('')
    setLiveHealthDiagnostics(null)
    setLiveHealthResult(null)
    try {
      const response = await apiFetch(liveHealthAnalysisUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ applicationName: 'app-inventory-example-dev', question: healthForm.question.trim(), metricProfile: 'appServiceBasicHealth', analysisProfile: 'appServiceWithApplicationInsights', timeWindow: 'last30Minutes' }),
      })
      if (!response.ok) {
        let diagnostics: Record<string, string> = { httpStatus: String(response.status), failureType: 'httpError' }
        try {
          const body = await response.json() as Record<string, unknown>
          for (const field of ['errorCode', 'failureStage', 'semanticRule'])
            if (typeof body[field] === 'string' && body[field].length <= 100) diagnostics[field] = body[field] as string
        } catch { /* Keep status-only diagnostics when the body is not a safe problem document. */ }
        setLiveHealthDiagnostics(diagnostics)
        setHealthMessage(response.status === 503 || response.status === 504
          ? 'Live Azure health data is temporarily unavailable. Please try again later.'
          : response.status === 401 || response.status === 403
            ? 'You are not authorized to use live application health analysis.'
            : 'Live application health analysis is unavailable right now.')
        return
      }
      try {
        setLiveHealthResult((await response.json()) as LiveHealthResponse)
      } catch {
        setLiveHealthDiagnostics({ failureType: 'responseParsingFailure' })
        setHealthMessage('Live application health analysis returned an unreadable response.')
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : ''
      const parts = message.split(':')
      const diagnostics: Record<string, string> = { failureType: /MSAL_/.test(message) ? 'authenticationFailure' : /cancel/i.test(message) ? 'requestCancelled' : 'networkFailure' }
      if (parts[0] === 'MSAL_INTERACTION_REQUIRED' || parts[0] === 'MSAL_TOKEN_ACQUISITION_FAILED') {
        diagnostics.msalErrorCode = parts[1].slice(0, 80)
        diagnostics.msalSubError = (parts[2] ?? 'none').slice(0, 80)
      }
      setLiveHealthDiagnostics(diagnostics)
      setHealthMessage('Live application health analysis is unavailable right now. Please try again later.')
    } finally {
      setAnalyzingLiveHealth(false)
    }
  }

  async function troubleshootDeployment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!account || authBusy || troubleshooting) return
    const runId = Number(troubleshootingForm.runId)
    if (!troubleshootingForm.pipelineName.trim() || !/^\d+$/.test(troubleshootingForm.runId) || runId <= 0 ||
        troubleshootingForm.errorText.trim().length < 10) {
      setTroubleshootingMessage('Enter a pipeline name, a positive Run ID, and at least 10 characters of error text.')
      return
    }

    setTroubleshooting(true)
    setTroubleshootingMessage('')
    setTroubleshootingResult(null)
    const contextValues = {
      service: troubleshootingForm.service.trim() || null,
      resourceType: troubleshootingForm.azureResourceType.trim() || null,
      resourceName: troubleshootingForm.azureResourceName.trim() || null,
      resourceGroup: troubleshootingForm.resourceGroup.trim() || null,
      region: troubleshootingForm.region.trim() || null,
    }
    const hasAzureContext = Object.values(contextValues).some(Boolean)
    try {
      const response = await apiFetch(troubleshootingUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          pipelineName: troubleshootingForm.pipelineName.trim(),
          runId,
          failedStage: troubleshootingForm.failedStage.trim() || null,
          failedJob: troubleshootingForm.failedJob.trim() || null,
          failedTask: troubleshootingForm.failedTask.trim() || null,
          errorText: troubleshootingForm.errorText,
          terraformCommand: troubleshootingForm.terraformCommand.trim() || null,
          azureContext: hasAzureContext ? contextValues : null,
        }),
      })
      if (!response.ok) {
        setTroubleshootingMessage(response.status === 400
          ? 'Check the troubleshooting evidence and try again.'
          : response.status === 401 || response.status === 403
            ? 'You are not authorized to use deployment troubleshooting.'
            : 'Deployment troubleshooting is unavailable right now. Please try again later.')
        return
      }
      setTroubleshootingResult((await response.json()) as DeploymentTroubleshootingResponse)
      setTroubleshootingForm(current => ({ ...current, errorText: '' }))
    } catch {
      setTroubleshootingMessage('Deployment troubleshooting is unavailable right now. Please try again later.')
    } finally {
      setTroubleshooting(false)
    }
  }

  async function reviewInfrastructure(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!account || authBusy || reviewing) return
    setReviewing(true)
    setReviewMessage('')
    setAiReview(null)
    try {
      const currentRequest = canSupplyCurrentRequest(resourceType, applicationName, runtime, environment, description)
        ? {
            resourceType,
            applicationName,
            runtime,
            environment,
            description: description || null,
          }
        : null
      const response = await apiFetch(aiReviewUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          intent: reviewIntent,
          currentRequest,
        }),
      })
      if (!response.ok) {
        setReviewMessage(response.status === 400
          ? 'The request details or intent are not ready for review. Check the form and try again.'
          : response.status === 401 || response.status === 403
            ? 'You are not authorized to use the AI review.'
            : 'The AI review is unavailable right now. Please try again later.')
        return
      }
      setAiReview((await response.json()) as AiInfrastructureReview)
    } catch {
      setReviewMessage('The AI review is unavailable right now. Please try again later.')
    } finally {
      setReviewing(false)
    }
  }

  async function findRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const requestId = lookupRequestId.trim()
    if (!requestId) {
      setLookupMessage('Enter a Request ID to find a request.')
      return
    }
    if (!account || authBusy || lookingUp) return
    setLookingUp(true)
    setLookupMessage('')
    try {
      const response = await apiFetch(`${apiUrl.replace(/\/$/, '')}/${encodeURIComponent(requestId)}`, { method: 'GET' })
      if (response.status === 404) {
        setLookupMessage('No request was found with that ID.')
      } else if (response.status === 401) {
        setLookupMessage('Please sign in again to find this request.')
      } else if (response.status === 403) {
        setLookupMessage('You do not have permission to view this request.')
      } else if (!response.ok) {
        setLookupMessage('The request could not be found right now. Please try again later.')
      } else {
        const found = (await response.json()) as ApplicationRequest
        if (found.requestId?.toLowerCase() !== requestId.toLowerCase() || typeof found.status !== 'string') {
          setLookupMessage('The request could not be displayed. Please try again later.')
        } else {
          setCreatedRequest(found)
          setRefreshMessage('')
        }
      }
    } catch {
      setLookupMessage('The request could not be found right now. Please try again later.')
    } finally {
      setLookingUp(false)
    }
  }

  async function refreshStatus() {
    if (!createdRequest || !account || authBusy || refreshingStatus) return
    setRefreshingStatus(true)
    setRefreshMessage('')
    try {
      const response = await apiFetch(`${apiUrl.replace(/\/$/, '')}/${encodeURIComponent(createdRequest.requestId)}`, { method: 'GET' })
      if (response.status === 401) {
        setRefreshMessage('Please sign in again to refresh the status.')
      } else if (response.status === 403) {
        setRefreshMessage('You do not have permission to view this request.')
      } else if (!response.ok) {
        setRefreshMessage('Status could not be refreshed. Please try again later.')
      } else {
        const updated = (await response.json()) as ApplicationRequest
        if (updated.requestId !== createdRequest.requestId || typeof updated.status !== 'string') {
          setRefreshMessage('Status could not be refreshed. Please try again later.')
        } else {
          setCreatedRequest(updated)
        }
      }
    } catch {
      setRefreshMessage('Status could not be refreshed. Please try again later.')
    } finally {
      setRefreshingStatus(false)
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setErrors([])
    setCreatedRequest(null)
    setRefreshMessage('')
    if (!account || authBusy) {
      setErrors(['Sign in before submitting a request.'])
      return
    }
    setSubmitting(true)

    try {
      const response = await apiFetch(apiUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          resourceType,
          applicationName,
          runtime,
          environment,
          description: description || null,
        }),
      })

      if (response.status === 201) {
        setCreatedRequest((await response.json()) as ApplicationRequest)
        return
      }

      if (response.status === 401) {
        setErrors(['Your API sign-in is missing or expired. Please sign in again.'])
        return
      }
      if (response.status === 403) {
        setErrors(['You are signed in but do not have permission to submit requests. Contact the lab administrator.'])
        return
      }

      if (response.status === 400) {
        const problem = (await response.json()) as ValidationProblem
        const messages = Object.values(problem.errors ?? {}).flat()
        setErrors(messages.length > 0 ? messages : [problem.title ?? 'Please check your request.'])
        return
      }

      if (response.status === 502) {
        const problem = await response.json() as { title?: string; detail?: string; requestId?: string }
        setErrors([problem.title ?? 'Pipeline queueing failed.', problem.detail ?? 'Check the saved request before resubmitting.', ...(problem.requestId ? [`Request ID: ${problem.requestId}`] : [])])
        return
      }

      setErrors([`The request could not be created (HTTP ${response.status}). Please try again.`])
    } catch (error) {
      setErrors([error instanceof TypeError ? 'Cannot connect to the AIDP API. Check that it is running and its HTTPS certificate is trusted.' : error instanceof Error ? error.message : 'The request could not be completed.'])
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="portal-shell">
      <header className="site-header">
        <div className="brand-mark" aria-hidden="true">A</div>
        <div className="brand-copy">
          <strong>AIDP</strong>
          <span>Azure Intelligent Developer Platform</span>
        </div>
        <button className="auth-button" type="button" disabled={authBusy || submitting} onClick={account ? signOut : signIn}>
          {account ? 'Sign out' : 'Sign in'}
        </button>
      </header>

      <main className="main-content">
        <div className="page-heading">
          <p className="eyebrow">Self-service catalog</p>
          <h1>Create Application</h1>
          <p>Tell us what you need to build. The platform will validate your request and track its progress.</p>
        </div>

        <section className="card ai-review-card" aria-labelledby="ai-review-heading">
          <div className="card-heading">
            <div>
              <p className="section-kicker">Advisory review</p>
              <h2 id="ai-review-heading">AI Infrastructure Review</h2>
            </div>
          </div>
          <form className="ai-review-form" onSubmit={reviewInfrastructure}>
            <div className="field">
              <label htmlFor="review-intent">Describe what you want to build</label>
              <textarea
                id="review-intent"
                value={reviewIntent}
                onChange={(event) => setReviewIntent(event.target.value)}
                placeholder="I need a public .NET API for inventory that stores files and will be used by about 50 internal users."
                rows={5}
                minLength={20}
                maxLength={2000}
              />
              <span className="field-hint character-count">{reviewIntent.length} / 2000</span>
            </div>
            <button className="submit-button" type="submit" disabled={reviewing || authBusy || !account || reviewIntent.trim().length < 20}>
              {reviewing ? 'Reviewing…' : 'Review with AI'}
            </button>
            {!account && <p>Sign in to request an infrastructure review.</p>}
            <p className="field-hint">AI recommendations are advisory. Platform validation and approval remain authoritative.</p>
            {reviewMessage && <p className="message error-message" role="alert">{reviewMessage}</p>}
          </form>

          {aiReview && (
            <div className="ai-review-result" aria-live="polite">
              <section className="review-section">
                <h3>Summary</h3>
                <p>{aiReview.summary}</p>
                <dl className="review-metadata">
                  <div><dt>Risk level</dt><dd><span className={`review-badge badge-${aiReview.riskLevel}`}>{displayValue(aiReview.riskLevel)}</span></dd></div>
                  <div><dt>Confidence</dt><dd><span className="review-badge">{displayValue(aiReview.confidence.level)}</span><span>{aiReview.confidence.rationale}</span></dd></div>
                </dl>
              </section>

              <section className="review-section">
                <h3>Recommended Architecture</h3>
                <div className="review-card-list">
                  {aiReview.recommendedArchitecture.map((item, index) => (
                    <article className="review-item" key={`${item.component}-${index}`}>
                      <div className="review-item-heading">
                        <h4>{item.component}</h4>
                        <span className={`review-badge ${item.supportStatus === 'supported' ? 'badge-supported' : 'badge-recommendation'}`}>
                          {item.supportStatus === 'supported' ? 'Supported' : 'Recommendation only'}
                        </span>
                      </div>
                      <p>{item.purpose}</p>
                      <p><strong>Recommendation:</strong> {item.recommendation}</p>
                    </article>
                  ))}
                </div>
              </section>

              {([
                ['Security Findings', aiReview.securityFindings],
                ['Reliability Findings', aiReview.reliabilityFindings],
                ['Cost Considerations', aiReview.costConsiderations],
                ['Missing Information', aiReview.missingInformation],
              ] as [string, AiFinding[]][]).map(([heading, findings]) => (
                <section className="review-section" key={heading}>
                  <h3>{heading}</h3>
                  <div className="review-card-list">
                    {findings.map((finding, index) => (
                      <article className="review-item" key={`${heading}-${index}`}>
                        <div className="review-item-heading">
                          <h4>{finding.finding}</h4>
                          <span className={`review-badge badge-${finding.severity}`}>{displayValue(finding.severity)}</span>
                        </div>
                        <p><strong>Recommendation:</strong> {finding.recommendation}</p>
                        {finding.evidence.length > 0 && (
                          <div className="review-evidence">
                            <strong>Evidence</strong>
                            <ul>{finding.evidence.map((evidence, evidenceIndex) => <li key={evidenceIndex}>{evidence.statement}</li>)}</ul>
                          </div>
                        )}
                      </article>
                    ))}
                  </div>
                </section>
              ))}

              <section className="review-section review-next-steps">
                <h3>Recommended Next Steps</h3>
                <ol>{aiReview.recommendedNextSteps.map((step, index) => <li key={index}>{step}</li>)}</ol>
              </section>
            </div>
          )}
        </section>

        <section className="card ai-review-card" aria-labelledby="troubleshooting-heading">
          <div className="card-heading">
            <div>
              <p className="section-kicker">Read-only advisor</p>
              <h2 id="troubleshooting-heading">AI Deployment Troubleshooting</h2>
            </div>
          </div>
          <p className="advisory-note">This assistant analyzes only the evidence you provide. It cannot rerun pipelines, execute Terraform, or change Azure resources.</p>
          <form className="ai-review-form" onSubmit={troubleshootDeployment}>
            <div className="field-row">
              <div className="field">
                <label htmlFor="troubleshoot-pipeline">Pipeline Name</label>
                <input id="troubleshoot-pipeline" value={troubleshootingForm.pipelineName} onChange={event => updateTroubleshootingField('pipelineName', event.target.value)} maxLength={200} required />
              </div>
              <div className="field">
                <label htmlFor="troubleshoot-run">Run ID</label>
                <input id="troubleshoot-run" type="number" min="1" step="1" value={troubleshootingForm.runId} onChange={event => updateTroubleshootingField('runId', event.target.value)} required />
              </div>
            </div>
            <div className="troubleshooting-grid">
              {([
                ['failedStage', 'Failed Stage'],
                ['failedJob', 'Failed Job'],
                ['failedTask', 'Failed Task'],
              ] as [keyof typeof troubleshootingForm, string][]).map(([field, label]) => (
                <div className="field" key={field}>
                  <label htmlFor={`troubleshoot-${field}`}>{label} <span className="optional">Optional</span></label>
                  <input id={`troubleshoot-${field}`} value={troubleshootingForm[field]} onChange={event => updateTroubleshootingField(field, event.target.value)} maxLength={200} />
                </div>
              ))}
            </div>
            <div className="field">
              <label htmlFor="troubleshoot-error">Error Text</label>
              <span className="security-hint">Do not paste passwords, tokens, connection strings, or other credentials. The API also applies credential redaction before AI analysis.</span>
              <textarea id="troubleshoot-error" rows={8} minLength={10} maxLength={12000} value={troubleshootingForm.errorText} onChange={event => updateTroubleshootingField('errorText', event.target.value)} required />
              <span className="field-hint character-count">{troubleshootingForm.errorText.length} / 12000</span>
            </div>
            <div className="field">
              <label htmlFor="troubleshoot-terraform">Terraform Command <span className="optional">Optional</span></label>
              <input id="troubleshoot-terraform" value={troubleshootingForm.terraformCommand} onChange={event => updateTroubleshootingField('terraformCommand', event.target.value)} maxLength={500} placeholder="terraform init -backend=false" />
            </div>
            <details className="optional-context">
              <summary>Azure context <span className="optional">Optional</span></summary>
              <div className="troubleshooting-grid context-grid">
                {([
                  ['service', 'Service', 100],
                  ['azureResourceType', 'Resource Type', 200],
                  ['azureResourceName', 'Resource Name', 260],
                  ['resourceGroup', 'Resource Group', 90],
                  ['region', 'Region', 100],
                ] as [keyof typeof troubleshootingForm, string, number][]).map(([field, label, maxLength]) => (
                  <div className="field" key={field}>
                    <label htmlFor={`troubleshoot-${field}`}>{label}</label>
                    <input id={`troubleshoot-${field}`} value={troubleshootingForm[field]} onChange={event => updateTroubleshootingField(field, event.target.value)} maxLength={maxLength} />
                  </div>
                ))}
              </div>
            </details>
            <button className="submit-button" type="submit" disabled={troubleshooting || authBusy || !account}>
              {troubleshooting ? 'Analyzing evidence…' : 'Analyze Deployment Failure'}
            </button>
            {!account && <p>Sign in to analyze deployment evidence.</p>}
            {troubleshootingMessage && <p className="message error-message" role="alert">{troubleshootingMessage}</p>}
          </form>

          {troubleshootingResult && (
            <div className="ai-review-result" aria-live="polite">
              <section className="review-section">
                <h3>Summary</h3>
                <p>{troubleshootingResult.summary}</p>
                <dl className="review-metadata troubleshooting-metadata">
                  <div><dt>Failure category</dt><dd><span className="review-badge">{displayValue(troubleshootingResult.failureCategory)}</span></dd></div>
                  <div><dt>Overall confidence</dt><dd><span className={`review-badge badge-${troubleshootingResult.overallConfidence.level}`}>{displayValue(troubleshootingResult.overallConfidence.level)}</span><span>{troubleshootingResult.overallConfidence.rationale}</span></dd></div>
                  <div><dt>Credential redaction</dt><dd><span className={`review-badge ${troubleshootingResult.suppliedEvidence.redactionsApplied ? 'badge-medium' : 'badge-supported'}`}>{troubleshootingResult.suppliedEvidence.redactionsApplied ? 'Applied' : 'Not needed'}</span></dd></div>
                </dl>
              </section>

              <section className="review-section">
                <h3>Findings</h3>
                <div className="review-card-list">
                  {troubleshootingResult.findings.map((finding, index) => (
                    <article className="review-item" key={index}>
                      <div className="review-item-heading">
                        <h4>{finding.finding}</h4>
                        <span className={`review-badge badge-${finding.confidence.level}`}>{displayValue(finding.confidence.level)} confidence</span>
                      </div>
                      <p>{finding.confidence.rationale}</p>
                      <div className="review-evidence">
                        <strong>Evidence</strong>
                        <ul>{finding.evidence.map((evidence, evidenceIndex) => <li key={evidenceIndex}><span className="evidence-type">{displayValue(evidence.type)}:</span> {evidence.statement}</li>)}</ul>
                      </div>
                      <p className="recommended-investigation"><strong>Recommended investigation:</strong> {finding.recommendedInvestigation}</p>
                    </article>
                  ))}
                </div>
              </section>

              <section className="review-section review-next-steps">
                <h3>Missing Information</h3>
                {troubleshootingResult.missingInformation.length > 0
                  ? <ul>{troubleshootingResult.missingInformation.map((item, index) => <li key={index}>{item}</li>)}</ul>
                  : <p>None identified.</p>}
              </section>
              <section className="review-section review-next-steps">
                <h3>Recommended Next Steps</h3>
                <ol>{troubleshootingResult.recommendedNextSteps.map((step, index) => <li key={index}>{step}</li>)}</ol>
              </section>
            </div>
          )}
        </section>

        <section className="card ai-review-card" aria-labelledby="health-heading">
          <div className="card-heading">
            <div>
              <p className="section-kicker">Read-only health advisor</p>
              <h2 id="health-heading">AI Application Health</h2>
            </div>
          </div>
          <p className="advisory-note">This assistant analyzes supplied evidence only. It does not query Azure or perform remediation.</p>
          <form className="ai-review-form health-form" onSubmit={analyzeHealth}>
            <div className="troubleshooting-grid">
              <div className="field">
                <label htmlFor="health-application">Application Name</label>
                <input id="health-application" value={healthForm.applicationName} onChange={event => updateHealthField('applicationName', event.target.value)} minLength={3} maxLength={100} required />
              </div>
              <div className="field">
                <label htmlFor="health-environment">Environment</label>
                <input id="health-environment" value={healthForm.environment} onChange={event => updateHealthField('environment', event.target.value)} maxLength={50} required />
              </div>
              <div className="field">
                <label htmlFor="health-service">Azure Service <span className="optional">Optional</span></label>
                <select id="health-service" value={healthForm.azureService} onChange={event => updateHealthField('azureService', event.target.value)}>
                  <option value="">Not specified</option>
                  <option value="appservice">App Service</option>
                </select>
              </div>
            </div>
            <div className="field">
              <label htmlFor="health-question">Question</label>
              <textarea id="health-question" rows={3} minLength={20} maxLength={1000} value={healthForm.question} onChange={event => updateHealthField('question', event.target.value)} placeholder="Why is this App Service slow and returning 5xx responses?" required />
              <span className="field-hint character-count">{healthForm.question.length} / 1000</span>
            </div>
            <details className="optional-context">
              <summary>Target metadata and observation window <span className="optional">Optional</span></summary>
              <div className="troubleshooting-grid context-grid">
                {([
                  ['resourceName', 'Resource Name'], ['resourceGroup', 'Resource Group'], ['region', 'Region'],
                ] as [keyof typeof healthForm, string][]).map(([field, label]) => (
                  <div className="field" key={field}>
                    <label htmlFor={`health-${field}`}>{label}</label>
                    <input id={`health-${field}`} value={healthForm[field]} onChange={event => updateHealthField(field, event.target.value)} maxLength={260} />
                  </div>
                ))}
                <div className="field">
                  <label htmlFor="health-window-start">Window Start</label>
                  <input id="health-window-start" type="datetime-local" value={healthForm.windowStart} onChange={event => updateHealthField('windowStart', event.target.value)} />
                </div>
                <div className="field">
                  <label htmlFor="health-window-end">Window End</label>
                  <input id="health-window-end" type="datetime-local" value={healthForm.windowEnd} onChange={event => updateHealthField('windowEnd', event.target.value)} />
                </div>
              </div>
            </details>

            <div className="evidence-editor-heading">
              <div><h3>Evidence</h3><p>Add the observations available for this investigation.</p></div>
              <button className="secondary-button" type="button" onClick={() => setHealthEvidence(current => [...current, newHealthEvidence()])} disabled={healthEvidence.length >= 30}>Add Evidence</button>
            </div>
            <p className="security-hint">Do not paste passwords, tokens, connection strings, or other credentials. The API applies credential redaction before AI analysis.</p>
            <div className="evidence-editor-list">
              {healthEvidence.map((item, index) => (
                <fieldset className="evidence-editor" key={item.id}>
                  <legend>Evidence {index + 1}</legend>
                  <div className="evidence-editor-actions">
                    {healthEvidence.length > 1 && <button className="text-button" type="button" onClick={() => setHealthEvidence(current => current.filter(candidate => candidate.id !== item.id))}>Remove</button>}
                  </div>
                  <div className="troubleshooting-grid">
                    <div className="field">
                      <label htmlFor={`health-evidence-type-${item.id}`}>Evidence Type</label>
                      <select id={`health-evidence-type-${item.id}`} value={item.type} onChange={event => updateHealthEvidence(item.id, { type: event.target.value })}>
                        <option value="azureMetric">Azure Metric</option>
                        <option value="applicationInsights">Application Insights</option>
                        <option value="appServiceLog">App Service Log</option>
                        <option value="deploymentMetadata">Deployment Metadata</option>
                        <option value="resourceHealth">Resource Health</option>
                        <option value="configurationMetadata">Configuration Metadata</option>
                      </select>
                    </div>
                    <div className="field">
                      <label htmlFor={`health-evidence-observed-${item.id}`}>Observed At <span className="optional">Optional</span></label>
                      <input id={`health-evidence-observed-${item.id}`} type="datetime-local" value={item.observedAt} onChange={event => updateHealthEvidence(item.id, { observedAt: event.target.value })} />
                    </div>
                  </div>
                  <div className="field">
                    <label htmlFor={`health-evidence-title-${item.id}`}>Title</label>
                    <input id={`health-evidence-title-${item.id}`} value={item.title} onChange={event => updateHealthEvidence(item.id, { title: event.target.value })} maxLength={200} required />
                  </div>
                  <div className="field">
                    <label htmlFor={`health-evidence-content-${item.id}`}>Content</label>
                    <textarea id={`health-evidence-content-${item.id}`} rows={5} minLength={10} maxLength={8000} value={item.content} onChange={event => updateHealthEvidence(item.id, { content: event.target.value })} required />
                    <span className="field-hint character-count">{item.content.length} / 8000</span>
                  </div>

                  {item.type === 'azureMetric' && (
                    <div className="metric-editor">
                      <div className="metric-editor-heading">
                        <strong>Structured metrics <span className="optional">Optional</span></strong>
                        <button className="text-button" type="button" onClick={() => updateHealthEvidence(item.id, { metrics: [...item.metrics, newHealthMetric()] })}>Add Metric</button>
                      </div>
                      {item.metrics.map((metric, metricIndex) => (
                        <div className="metric-row" key={metric.id}>
                          <div className="field">
                            <label htmlFor={`metric-name-${metric.id}`}>Metric Name</label>
                            <select id={`metric-name-${metric.id}`} value={metric.name} onChange={event => {
                              const name = event.target.value
                              updateHealthMetric(item.id, metric.id, { name, unit: metricUnits[name][0] })
                            }}>
                              {['requests', 'http5xx', 'http4xx', 'averageResponseTime', 'cpuPercentage', 'memoryWorkingSetBytes', 'dependencyFailures', 'availability'].map(name => <option value={name} key={name}>{displayValue(name)}</option>)}
                            </select>
                          </div>
                          <div className="field">
                            <label htmlFor={`metric-aggregation-${metric.id}`}>Aggregation</label>
                            <select id={`metric-aggregation-${metric.id}`} value={metric.aggregation} onChange={event => updateHealthMetric(item.id, metric.id, { aggregation: event.target.value })}>
                              {['count', 'average', 'minimum', 'maximum', 'total', 'percentage'].map(value => <option value={value} key={value}>{displayValue(value)}</option>)}
                            </select>
                          </div>
                          <div className="field">
                            <label htmlFor={`metric-value-${metric.id}`}>Value</label>
                            <input id={`metric-value-${metric.id}`} type="number" step="any" value={metric.value} onChange={event => updateHealthMetric(item.id, metric.id, { value: event.target.value })} required />
                          </div>
                          <div className="field">
                            <label htmlFor={`metric-unit-${metric.id}`}>Unit</label>
                            <select id={`metric-unit-${metric.id}`} value={metric.unit} onChange={event => updateHealthMetric(item.id, metric.id, { unit: event.target.value })}>
                              {metricUnits[metric.name].map(unit => <option value={unit} key={unit}>{unit}</option>)}
                            </select>
                          </div>
                          <div className="field">
                            <label htmlFor={`metric-observed-${metric.id}`}>Observed At <span className="optional">Optional</span></label>
                            <input id={`metric-observed-${metric.id}`} type="datetime-local" value={metric.observedAt} onChange={event => updateHealthMetric(item.id, metric.id, { observedAt: event.target.value })} />
                          </div>
                          <button className="text-button metric-remove" type="button" aria-label={`Remove metric ${metricIndex + 1}`} onClick={() => updateHealthEvidence(item.id, { metrics: item.metrics.filter(candidate => candidate.id !== metric.id) })}>Remove</button>
                        </div>
                      ))}
                    </div>
                  )}
                </fieldset>
              ))}
            </div>
            <button className="submit-button" type="submit" disabled={analyzingHealth || authBusy || !account}>
              {analyzingHealth ? 'Analyzing health…' : 'Analyze Application Health'}
            </button>
            <div className="live-health-control">
              <strong>Live Azure Analysis</strong>
              <p className="field-hint">Uses the allowlisted inventory App Service and read-only Azure evidence.</p>
              <button className="secondary-button" type="button" onClick={analyzeLiveHealth} disabled={analyzingLiveHealth || authBusy || !account || healthForm.question.trim().length < 20}>
                {analyzingLiveHealth ? 'Collecting live health…' : 'Analyze Live Azure Health'}
              </button>
            </div>
            {!account && <p>Sign in to analyze application health evidence.</p>}
            {healthMessage && <p className="message error-message" role="alert">{healthMessage}</p>}
            {liveHealthDiagnostics && <details className="technical-details"><summary>Technical details</summary><dl>{Object.entries(liveHealthDiagnostics).map(([key, value]) => <div key={key}><dt>{displayValue(key)}</dt><dd>{value}</dd></div>)}</dl></details>}
          </form>

          {healthResult && (
            <div className="ai-review-result" aria-live="polite">
              <section className="review-section">
                <h3>Summary</h3>
                <p>{healthResult.summary}</p>
                <dl className="review-metadata troubleshooting-metadata">
                  <div><dt>Overall health</dt><dd><span className={`review-badge health-${healthResult.overallHealthAssessment}`}>{displayValue(healthResult.overallHealthAssessment)}</span></dd></div>
                  <div><dt>Overall confidence</dt><dd><span className={`review-badge badge-${healthResult.overallConfidence.level}`}>{displayValue(healthResult.overallConfidence.level)}</span><span>{healthResult.overallConfidence.rationale}</span></dd></div>
                  <div><dt>Credential redaction</dt><dd><span className={`review-badge ${healthResult.suppliedEvidence.redactionsApplied ? 'badge-medium' : 'badge-supported'}`}>{healthResult.suppliedEvidence.redactionsApplied ? 'Applied' : 'Not needed'}</span></dd></div>
                </dl>
              </section>
              <section className="review-section">
                <h3>Findings</h3>
                <div className="review-card-list">
                  {healthResult.findings.map((finding, index) => (
                    <article className="review-item" key={index}>
                      <div className="review-item-heading">
                        <h4>{finding.finding}</h4>
                        <div className="finding-badges">
                          <span className={`review-badge conclusion-${finding.conclusionType}`}>{displayValue(finding.conclusionType)}</span>
                          <span className={`review-badge badge-${finding.severity}`}>{displayValue(finding.severity)}</span>
                        </div>
                      </div>
                      <p><strong>Category:</strong> {displayValue(finding.category)}</p>
                      <p><strong>Confidence:</strong> {displayValue(finding.confidence.level)} — {finding.confidence.rationale}</p>
                      <div className="review-evidence"><strong>Evidence</strong><ul>{finding.evidence.map((evidence, evidenceIndex) => <li key={evidenceIndex}><span className="evidence-type">{displayValue(evidence.provenance)}:</span> {evidence.statement}</li>)}</ul></div>
                      <p className="recommended-investigation"><strong>Recommended investigation:</strong> {finding.recommendedInvestigation}</p>
                    </article>
                  ))}
                </div>
              </section>
              <section className="review-section review-next-steps">
                <h3>Missing Information</h3>
                {healthResult.missingInformation.length > 0 ? <ul>{healthResult.missingInformation.map((item, index) => <li key={index}>{item}</li>)}</ul> : <p>None identified.</p>}
              </section>
              <section className="review-section review-next-steps">
                <h3>Recommended Next Steps</h3>
                <ol>{healthResult.recommendedNextSteps.map((step, index) => <li key={index}>{step}</li>)}</ol>
              </section>
            </div>
          )}
          {liveHealthResult && (
            <div className="ai-review-result live-health-result" aria-live="polite">
              <section className="review-section">
                <h3>Live Azure Analysis</h3>
                <dl className="review-metadata troubleshooting-metadata">
                  <div><dt>Collection status</dt><dd><span className="review-badge badge-supported">{displayValue(liveHealthResult.collection.status)}</span></dd></div>
                  <div><dt>Azure Monitor evidence</dt><dd>{liveHealthResult.collection.collectedMetricCount} of {liveHealthResult.collection.requestedMetricCount} metrics collected</dd></div>
                  {liveHealthResult.applicationInsightsCollection && <div><dt>Application Insights evidence</dt><dd><span className="review-badge badge-supported">{displayValue(liveHealthResult.applicationInsightsCollection.status)}</span> {liveHealthResult.applicationInsightsCollection.returnedSummaryCount} summaries</dd></div>}
                </dl>
              </section>
              {liveHealthResult.analysis && <>
                <section className="review-section"><h3>Health assessment</h3><p>{liveHealthResult.analysis.summary}</p><dl className="review-metadata troubleshooting-metadata"><div><dt>Overall health</dt><dd><span className={`review-badge health-${liveHealthResult.analysis.overallHealthAssessment}`}>{displayValue(liveHealthResult.analysis.overallHealthAssessment)}</span></dd></div><div><dt>Overall confidence</dt><dd><span className={`review-badge badge-${liveHealthResult.analysis.overallConfidence.level}`}>{displayValue(liveHealthResult.analysis.overallConfidence.level)}</span> {liveHealthResult.analysis.overallConfidence.rationale}</dd></div></dl></section>
                <section className="review-section"><h3>Findings</h3><div className="review-card-list">{liveHealthResult.analysis.findings.map((finding, index) => <article className="review-item" key={index}><div className="review-item-heading"><h4>{finding.finding}</h4><div className="finding-badges"><span className={`review-badge conclusion-${finding.conclusionType}`}>{displayValue(finding.conclusionType)}</span><span className={`review-badge badge-${finding.severity}`}>{displayValue(finding.severity)}</span></div></div><p><strong>Category:</strong> {displayValue(finding.category)}</p><p><strong>Confidence:</strong> {displayValue(finding.confidence.level)} — {finding.confidence.rationale}</p><div className="review-evidence"><strong>Evidence</strong><ul>{finding.evidence.map((evidence, evidenceIndex) => <li key={evidenceIndex}><span className="evidence-type">{displayValue(evidence.provenance)}:</span> {evidence.statement}</li>)}</ul></div><p className="recommended-investigation"><strong>Recommended investigation:</strong> {finding.recommendedInvestigation}</p></article>)}</div></section>
                <section className="review-section review-next-steps"><h3>Missing Information</h3>{liveHealthResult.analysis.missingInformation.length > 0 ? <ul>{liveHealthResult.analysis.missingInformation.map((item, index) => <li key={index}>{item}</li>)}</ul> : <p>None identified.</p>}<h3>Recommended Next Steps</h3><ol>{liveHealthResult.analysis.recommendedNextSteps.map((step, index) => <li key={index}>{step}</li>)}</ol></section>
              </>}
            </div>
          )}
        </section>

        <div className="content-grid">
          <section className="card form-card" aria-labelledby="form-heading">
            <div className="card-heading">
              <div>
                <p className="section-kicker">New request</p>
                <h2 id="form-heading">Application details</h2>
              </div>
              <span className="step-badge">01 / 01</span>
            </div>

            <form onSubmit={handleSubmit}>
              {!account && <p>Sign in to submit an application request.</p>}
              <div className="field">
                <label htmlFor="resource-type">Resource Type</label>
                <select id="resource-type" name="resourceType" value={resourceType} onChange={(event) => setResourceType(event.target.value)}>
                  <option value="appservice">App Service</option>
                </select>
              </div>

              <div className="field">
                <label htmlFor="application-name">Application Name</label>
                <input
                  id="application-name"
                  name="applicationName"
                  type="text"
                  value={applicationName}
                  onChange={(event) => setApplicationName(event.target.value)}
                  placeholder="my-application"
                  autoComplete="off"
                  aria-describedby="application-name-hint"
                />
                <span id="application-name-hint" className="field-hint">3–30 characters. Lowercase letters, numbers, and hyphens.</span>
              </div>

              <div className="field-row">
                <div className="field">
                  <label htmlFor="runtime">Runtime</label>
                  <select id="runtime" name="runtime" value={runtime} onChange={(event) => setRuntime(event.target.value)}>
                    <option value="dotnet10">.NET 10</option>
                  </select>
                </div>
                <div className="field">
                  <label htmlFor="environment">Environment</label>
                  <select id="environment" name="environment" value={environment} onChange={(event) => setEnvironment(event.target.value)}>
                    <option value="dev">Development</option>
                  </select>
                </div>
              </div>

              <div className="field">
                <label htmlFor="description">Description <span className="optional">Optional</span></label>
                <textarea
                  id="description"
                  name="description"
                  value={description}
                  onChange={(event) => setDescription(event.target.value)}
                  placeholder="What is this application for?"
                  rows={4}
                  maxLength={200}
                />
                <span className="field-hint character-count">{description.length} / 200</span>
              </div>

              {errors.length > 0 && (
                <div className="message error-message" role="alert">
                  <strong>We couldn't create this request.</strong>
                  <ul>{errors.map((error, index) => <li key={`${error}-${index}`}>{error}</li>)}</ul>
                </div>
              )}

              <button className="submit-button" type="submit" disabled={submitting || !account || authBusy}>
                {submitting ? 'Creating request…' : 'Create Application'}
                <span aria-hidden="true">→</span>
              </button>
            </form>
          </section>

          <aside className="side-column">
            <section className="card info-card">
              <div className="info-icon" aria-hidden="true">✓</div>
              <h2>A clearer path from idea to application.</h2>
              <p>Submit your application details here. AIDP checks that your request matches the options currently available.</p>
              <div className="info-divider" />
              <div className="info-note"><span className="note-dot" />Currently available: .NET 10 in Development</div>
            </section>

            {account && (
              <section className="card" aria-labelledby="lookup-heading">
                <h2 id="lookup-heading">Find a request</h2>
                <form onSubmit={findRequest}>
                  <div className="field">
                    <label htmlFor="lookup-request-id">Request ID</label>
                    <input id="lookup-request-id" type="text" value={lookupRequestId} onChange={(event) => setLookupRequestId(event.target.value)} placeholder="Paste a request ID" autoComplete="off" />
                  </div>
                  <button type="submit" disabled={lookingUp || authBusy}>{lookingUp ? 'Finding…' : 'Find Request'}</button>
                </form>
                {lookupMessage && <p role="status">{lookupMessage}</p>}
              </section>
            )}

            {createdRequest && (
              <section className="card result-card" aria-live="polite" aria-labelledby="result-heading">
                <p className="section-kicker success-kicker">Request details</p>
                <h2 id="result-heading">Your request is {createdRequest.status.replace(/([A-Z])/g, ' $1').toLowerCase()}</h2>
                <dl>
                  <div><dt>Request ID</dt><dd className="request-id">{createdRequest.requestId}</dd></div>
                  <div><dt>Application</dt><dd>{createdRequest.applicationName}</dd></div>
                  <div><dt>Resource Type</dt><dd>{createdRequest.resourceType === 'appservice' ? 'App Service' : createdRequest.resourceType}</dd></div>
                  <div><dt>Environment</dt><dd>{createdRequest.environment === 'dev' ? 'Development' : createdRequest.environment}</dd></div>
                  <div><dt>Runtime</dt><dd>{createdRequest.runtime === 'dotnet10' ? '.NET 10' : createdRequest.runtime}</dd></div>
                  <div><dt>Status</dt><dd><span className="status-pill">{createdRequest.status.replace(/([A-Z])/g, ' $1').replace(/^./, letter => letter.toUpperCase())}</span></dd></div>
                  {createdRequest.pipelineRunId != null && <div><dt>Pipeline Run ID</dt><dd>{createdRequest.pipelineRunId}</dd></div>}
                  <div><dt>Created</dt><dd>{new Date(createdRequest.createdAt).toLocaleString()}</dd></div>
                </dl>
                <button type="button" disabled={refreshingStatus || authBusy || !account} onClick={refreshStatus}>
                  {refreshingStatus ? 'Refreshing…' : 'Refresh Status'}
                </button>
                {createdRequest.statusRefreshError && <p role="status">Status could not be refreshed. Showing the last known status.</p>}
                {refreshMessage && <p role="status">{refreshMessage}</p>}
              </section>
            )}
          </aside>
        </div>
      </main>
    </div>
  )
}

export default App
