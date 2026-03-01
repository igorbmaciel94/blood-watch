import { FormEvent, useEffect, useMemo, useState } from "react";
import { Navigate, Route, Routes, useNavigate } from "react-router-dom";

type SourceItem = { source: string; name: string };
type RegionItem = { key: string; name: string };
type InstitutionItem = {
  id: string;
  name: string;
  code: string;
  region: RegionItem;
  municipality?: string | null;
  district?: string | null;
};
type NearestInstitutionItem = {
  distanceKm: number;
  institution: InstitutionItem;
};
type LatestReserveItem = {
  metric: string;
};

type SubscriptionItem = {
  id: string;
  source: string;
  type: string;
  scopeType: "region" | "institution";
  region?: string | null;
  institutionId?: string | null;
  metric?: string | null;
  target: string;
  isEnabled: boolean;
  createdAtUtc: string;
  disabledAtUtc?: string | null;
};

type DeliveryItem = {
  eventId: string;
  status: string;
  attemptCount: number;
  lastError?: string | null;
  createdAtUtc: string;
  sentAtUtc?: string | null;
};

type TokenResponse = {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
};

type ApiError = {
  title?: string;
  detail?: string;
  status?: number;
};

type CopilotDataBasisItem = {
  queryId: string;
  description: string;
};

type CopilotCitation = {
  queryId: string;
  resultIds: string[];
};

type CopilotAnswer = {
  shortAnswer: string;
  summaryBullets: string[];
  dataBasis: CopilotDataBasisItem[];
  citations: CopilotCitation[];
  disclaimer: string;
  generatedAtUtc: string;
  model: string;
};

type CopilotBriefingResponse = {
  briefingType: "daily" | "weekly";
  windowStartUtc: string;
  windowEndUtc: string;
  answer: CopilotAnswer;
};

type CopilotFeatureFlagResponse = {
  enabled: boolean;
  configuredEnabled: boolean;
  updatedAtUtc: string;
};

type AuthSession = {
  email: string;
  accessToken: string;
  expiresAtUtc: string;
};

const channelOptions = [
  { value: "discord:webhook", label: "Discord (Webhook URL)" },
  { value: "telegram:chat", label: "Telegram (Chat ID)" }
] as const;

const scopeOptions = [
  { value: "region", label: "Region scope" },
  { value: "institution", label: "Institution scope" }
] as const;

export function App() {
  const [session, setSession] = useState<AuthSession | null>(null);
  const [authNotice, setAuthNotice] = useState<string | null>(null);

  function handleAuthenticated(nextSession: AuthSession) {
    setSession(nextSession);
    setAuthNotice(null);
  }

  function handleLogout(message?: string) {
    setSession(null);
    setAuthNotice(message ?? null);
  }

  return (
    <Routes>
      <Route path="/" element={<Navigate to={session ? "/subscriptions" : "/login"} replace />} />
      <Route
        path="/login"
        element={
          session ? (
            <Navigate to="/subscriptions" replace />
          ) : (
            <LoginPage onAuthenticated={handleAuthenticated} notice={authNotice} clearNotice={() => setAuthNotice(null)} />
          )
        }
      />
      <Route
        path="/subscriptions"
        element={
          session ? (
            <SubscriptionsPage
              session={session}
              onLogout={(message) => handleLogout(message ?? "Session cleared.")}
              onAuthExpired={() => handleLogout("Session expired. Login again.")}
            />
          ) : (
            <Navigate to="/login" replace />
          )
        }
      />
      <Route
        path="/copilot"
        element={
          session ? (
            <CopilotPage
              onLogout={(message) => handleLogout(message ?? "Session cleared.")}
              onAuthExpired={() => handleLogout("Session expired. Login again.")}
            />
          ) : (
            <Navigate to="/login" replace />
          )
        }
      />
      <Route path="*" element={<Navigate to={session ? "/subscriptions" : "/login"} replace />} />
    </Routes>
  );
}

type LoginPageProps = {
  onAuthenticated: (session: AuthSession) => void;
  notice: string | null;
  clearNotice: () => void;
};

function LoginPage({ onAuthenticated, notice, clearNotice }: LoginPageProps) {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busyAuth, setBusyAuth] = useState(false);
  const [authMessage, setAuthMessage] = useState<string | null>(null);

  useEffect(() => {
    if (notice) {
      setAuthMessage(notice);
      clearNotice();
    }
  }, [clearNotice, notice]);

  async function onSignIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setAuthMessage(null);

    const trimmedEmail = email.trim().toLowerCase();
    if (trimmedEmail.length === 0 || password.trim().length === 0) {
      setAuthMessage("Email and password are required.");
      return;
    }

    setBusyAuth(true);
    try {
      const payload = await requestJson<TokenResponse>("/api/v1/auth/token", {
        method: "POST",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ email: trimmedEmail, password })
      });

      onAuthenticated({
        email: trimmedEmail,
        accessToken: payload.accessToken,
        expiresAtUtc: payload.expiresAtUtc
      });

      setPassword("");
      navigate("/subscriptions", { replace: true });
    } catch (error) {
      setAuthMessage(readError(error));
    } finally {
      setBusyAuth(false);
    }
  }

  return (
    <div className="auth-shell">
      <section className="auth-card">
        <p className="kicker">BloodWatch Console</p>
        <h1>Sign in</h1>

        <form onSubmit={onSignIn} className="stack">
          <label>
            Email
            <input
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="admin@email.com"
              autoComplete="username"
              required
            />
          </label>

          <label>
            Password
            <input
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              placeholder="password"
              autoComplete="current-password"
              required
            />
          </label>

          <button type="submit" disabled={busyAuth}>
            {busyAuth ? "Signing in..." : "Sign in"}
          </button>
        </form>

        {authMessage ? <p className="error">{authMessage}</p> : null}
      </section>
    </div>
  );
}

type SubscriptionsPageProps = {
  session: AuthSession;
  onLogout: (message?: string) => void;
  onAuthExpired: () => void;
};

function SubscriptionsPage({ session, onLogout, onAuthExpired }: SubscriptionsPageProps) {
  const navigate = useNavigate();
  const [sources, setSources] = useState<SourceItem[]>([]);
  const [selectedSource, setSelectedSource] = useState("");

  const [regions, setRegions] = useState<RegionItem[]>([]);
  const [institutions, setInstitutions] = useState<InstitutionItem[]>([]);
  const [metrics, setMetrics] = useState<string[]>([]);

  const [nearest, setNearest] = useState<NearestInstitutionItem[]>([]);
  const [geoMessage, setGeoMessage] = useState<string | null>(null);

  const [subscriptions, setSubscriptions] = useState<SubscriptionItem[]>([]);
  const [selectedSubscriptionId, setSelectedSubscriptionId] = useState<string | null>(null);
  const [deliveries, setDeliveries] = useState<DeliveryItem[]>([]);

  const [formType, setFormType] = useState<(typeof channelOptions)[number]["value"]>("discord:webhook");
  const [formTarget, setFormTarget] = useState("");
  const [formScopeType, setFormScopeType] = useState<(typeof scopeOptions)[number]["value"]>("region");
  const [formRegion, setFormRegion] = useState("");
  const [formInstitutionId, setFormInstitutionId] = useState("");
  const [formMetric, setFormMetric] = useState("");

  const [loadingCatalog, setLoadingCatalog] = useState(false);
  const [loadingSubscriptions, setLoadingSubscriptions] = useState(false);
  const [loadingDeliveries, setLoadingDeliveries] = useState(false);
  const [busyCreate, setBusyCreate] = useState(false);
  const [busyGeo, setBusyGeo] = useState(false);

  const [formMessage, setFormMessage] = useState<string | null>(null);
  const [subsMessage, setSubsMessage] = useState<string | null>(null);

  useEffect(() => {
    void loadSources();
  }, []);

  useEffect(() => {
    if (!selectedSource) {
      return;
    }

    void loadCatalog(selectedSource);
    void loadSubscriptions(selectedSource, session.accessToken);
  }, [selectedSource, session.accessToken]);

  useEffect(() => {
    if (!selectedSubscriptionId) {
      setDeliveries([]);
      return;
    }

    void loadDeliveries(selectedSubscriptionId, session.accessToken);
  }, [selectedSubscriptionId, session.accessToken]);

  const selectedSubscription = useMemo(
    () => subscriptions.find((entry) => entry.id === selectedSubscriptionId) ?? null,
    [selectedSubscriptionId, subscriptions]
  );
  const institutionsById = useMemo(
    () => new Map(institutions.map((institution) => [institution.id, institution])),
    [institutions]
  );

  async function loadSources() {
    try {
      const payload = await requestJson<{ items: SourceItem[] }>("/api/v1/sources");
      setSources(payload.items);
      if (payload.items.length > 0) {
        setSelectedSource(payload.items[0].source);
      }
    } catch (error) {
      setSubsMessage(readError(error));
    }
  }

  async function loadCatalog(source: string) {
    setLoadingCatalog(true);
    setGeoMessage(null);
    setNearest([]);

    const regionsPromise = requestJson<{ items: RegionItem[] }>(`/api/v1/regions?source=${encodeURIComponent(source)}`);
    const institutionsPromise = requestJson<{ items: InstitutionItem[] }>(
      `/api/v1/institutions?source=${encodeURIComponent(source)}`
    );
    const reservesPromise = requestJson<{ items: LatestReserveItem[] }>(
      `/api/v1/reserves/latest?source=${encodeURIComponent(source)}`
    );

    const [regionsResult, institutionsResult, reservesResult] = await Promise.allSettled([
      regionsPromise,
      institutionsPromise,
      reservesPromise
    ]);

    if (regionsResult.status === "fulfilled") {
      setRegions(regionsResult.value.items);
      if (regionsResult.value.items.length > 0) {
        setFormRegion((current) =>
          regionsResult.value.items.some((region) => region.key === current)
            ? current
            : regionsResult.value.items[0].key
        );
      }
    } else {
      setRegions([]);
      setFormRegion("");
    }

    if (institutionsResult.status === "fulfilled") {
      setInstitutions(institutionsResult.value.items);
      if (institutionsResult.value.items.length > 0) {
        setFormInstitutionId((current) =>
          institutionsResult.value.items.some((institution) => institution.id === current)
            ? current
            : institutionsResult.value.items[0].id
        );
      }
    } else {
      setInstitutions([]);
      setFormInstitutionId("");
    }

    if (reservesResult.status === "fulfilled") {
      const uniqueMetrics = [...new Set(reservesResult.value.items.map((entry) => entry.metric))].sort();
      setMetrics(uniqueMetrics);
    } else {
      setMetrics([]);
    }

    setLoadingCatalog(false);
  }

  async function loadSubscriptions(source: string, token: string) {
    setLoadingSubscriptions(true);
    setSubsMessage(null);

    try {
      const payload = await requestJson<{ items: SubscriptionItem[] }>(
        `/api/v1/subscriptions?source=${encodeURIComponent(source)}`,
        {
          headers: withBearer(token)
        }
      );
      setSubscriptions(payload.items);
      if (!payload.items.some((entry) => entry.id === selectedSubscriptionId)) {
        setSelectedSubscriptionId(payload.items[0]?.id ?? null);
      }
    } catch (error) {
      if (isAuthError(error)) {
        onAuthExpired();
      } else {
        setSubscriptions([]);
        setSelectedSubscriptionId(null);
        setSubsMessage(readError(error));
      }
    } finally {
      setLoadingSubscriptions(false);
    }
  }

  async function loadDeliveries(subscriptionId: string, token: string) {
    setLoadingDeliveries(true);
    try {
      const payload = await requestJson<{ items: DeliveryItem[] }>(
        `/api/v1/subscriptions/${subscriptionId}/deliveries?limit=10`,
        {
          headers: withBearer(token)
        }
      );
      setDeliveries(payload.items);
    } catch (error) {
      if (isAuthError(error)) {
        onAuthExpired();
      } else {
        setDeliveries([]);
        setSubsMessage(readError(error));
      }
    } finally {
      setLoadingDeliveries(false);
    }
  }

  async function onCreateSubscription(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFormMessage(null);

    const body: Record<string, unknown> = {
      source: selectedSource,
      type: formType,
      target: formTarget.trim(),
      scopeType: formScopeType,
      metric: formMetric.trim().length > 0 ? formMetric.trim() : null
    };

    if (formScopeType === "region") {
      body.region = formRegion;
      body.institutionId = null;
    } else {
      body.region = null;
      body.institutionId = formInstitutionId;
    }

    setBusyCreate(true);
    try {
      await requestJson<SubscriptionItem>("/api/v1/subscriptions", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...withBearer(session.accessToken)
        },
        body: JSON.stringify(body)
      });

      setFormTarget("");
      setFormMetric("");
      setFormMessage("Subscription created.");
      await loadSubscriptions(selectedSource, session.accessToken);
    } catch (error) {
      if (isAuthError(error)) {
        onAuthExpired();
      } else {
        setFormMessage(readError(error));
      }
    } finally {
      setBusyCreate(false);
    }
  }

  async function onDisableSubscription(subscriptionId: string) {
    setSubsMessage(null);

    try {
      await requestEmpty(`/api/v1/subscriptions/${subscriptionId}`, {
        method: "DELETE",
        headers: withBearer(session.accessToken)
      });
      await loadSubscriptions(selectedSource, session.accessToken);
    } catch (error) {
      if (isAuthError(error)) {
        onAuthExpired();
      } else {
        setSubsMessage(readError(error));
      }
    }
  }

  function onSuggestNearest() {
    if (!selectedSource) {
      setGeoMessage("Pick a source first.");
      return;
    }

    if (!("geolocation" in navigator)) {
      setGeoMessage("Browser geolocation is unavailable.");
      return;
    }

    setBusyGeo(true);
    setGeoMessage("Requesting browser location...");

    navigator.geolocation.getCurrentPosition(
      async (position) => {
        const latitude = position.coords.latitude.toFixed(6);
        const longitude = position.coords.longitude.toFixed(6);

        try {
          const payload = await requestJson<{ items: NearestInstitutionItem[] }>(
            `/api/v1/institutions/nearest?source=${encodeURIComponent(selectedSource)}&lat=${latitude}&lon=${longitude}&limit=5`
          );

          setNearest(payload.items);
          setGeoMessage(payload.items.length > 0 ? "Nearest institutions loaded." : "No nearby institutions found.");
        } catch (error) {
          setGeoMessage(readError(error));
        } finally {
          setBusyGeo(false);
        }
      },
      (error) => {
        setBusyGeo(false);
        setGeoMessage(`Location unavailable: ${error.message}`);
      },
      {
        enableHighAccuracy: false,
        timeout: 10000
      }
    );
  }

  function applyNearestSuggestion(item: NearestInstitutionItem) {
    setFormScopeType("institution");
    setFormInstitutionId(item.institution.id);
    setFormRegion(item.institution.region.key);
    setGeoMessage(`Suggestion selected: ${item.institution.name} (${item.distanceKm.toFixed(1)} km).`);
  }

  return (
    <div className="page">
      <header className="hero">
        <div className="hero-row">
          <div>
            <p className="kicker">BloodWatch Console</p>
            <h1>Subscriptions + Delivery Health</h1>
            <p>
              Create and manage <code>discord:webhook</code> and <code>telegram:chat</code> subscriptions. Use browser
              geolocation to quickly suggest nearby institutions.
            </p>
          </div>
          <div className="hero-actions">
            <button type="button" onClick={() => navigate("/copilot")}>Open Copilot</button>
            <button type="button" onClick={() => onLogout("Session cleared.")}>Sign out</button>
          </div>
        </div>
      </header>

      <main className="grid">
        <section className="panel">
          <h2>Create Subscription</h2>
          <form onSubmit={onCreateSubscription} className="stack">
            <label>
              Source
              <select value={selectedSource} onChange={(event) => setSelectedSource(event.target.value)}>
                {sources.map((source) => (
                  <option key={source.source} value={source.source}>
                    {source.name} ({source.source})
                  </option>
                ))}
              </select>
            </label>

            <label>
              Channel
              <select value={formType} onChange={(event) => setFormType(event.target.value as typeof formType)}>
                {channelOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              {formType === "discord:webhook" ? "Webhook URL" : "Chat ID"}
              <input
                type="text"
                value={formTarget}
                onChange={(event) => setFormTarget(event.target.value)}
                placeholder={formType === "discord:webhook" ? "https://discord.com/api/webhooks/..." : "-1001234567890"}
                required
              />
            </label>

            <label>
              Scope
              <select value={formScopeType} onChange={(event) => setFormScopeType(event.target.value as typeof formScopeType)}>
                {scopeOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            {formScopeType === "region" ? (
              <label>
                Region
                <select value={formRegion} onChange={(event) => setFormRegion(event.target.value)}>
                  {regions.map((region) => (
                    <option key={region.key} value={region.key}>
                      {region.name} ({region.key})
                    </option>
                  ))}
                </select>
              </label>
            ) : (
              <label>
                Institution
                <select value={formInstitutionId} onChange={(event) => setFormInstitutionId(event.target.value)}>
                  {institutions.map((institution) => (
                    <option key={institution.id} value={institution.id}>
                      {institution.name} ({institution.code})
                    </option>
                  ))}
                </select>
              </label>
            )}

            <label>
              Metric (optional)
              <input
                list="metric-options"
                value={formMetric}
                onChange={(event) => setFormMetric(event.target.value)}
                placeholder="blood-group-o-minus"
              />
              <datalist id="metric-options">
                {metrics.map((metric) => (
                  <option key={metric} value={metric} />
                ))}
              </datalist>
            </label>

            <div className="actions">
              <button type="submit" disabled={busyCreate || loadingCatalog || !selectedSource}>
                {busyCreate ? "Creating..." : "Create"}
              </button>
              <button type="button" onClick={onSuggestNearest} disabled={busyGeo || loadingCatalog || !selectedSource}>
                {busyGeo ? "Locating..." : "Suggest Nearest"}
              </button>
            </div>
          </form>

          {geoMessage && <p className="hint">{geoMessage}</p>}
          {nearest.length > 0 && (
            <ul className="nearest-list">
              {nearest.map((item) => (
                <li key={item.institution.id}>
                  <button type="button" onClick={() => applyNearestSuggestion(item)}>
                    {item.institution.name} • {item.institution.region.name} • {item.distanceKm.toFixed(1)} km
                  </button>
                </li>
              ))}
            </ul>
          )}

          {formMessage && <p className="message">{formMessage}</p>}
        </section>

        <section className="panel">
          <div className="panel-header">
            <h2>Subscriptions</h2>
            <button
              type="button"
              onClick={() => loadSubscriptions(selectedSource, session.accessToken)}
              disabled={loadingSubscriptions || !selectedSource}
            >
              Refresh
            </button>
          </div>

          {loadingSubscriptions ? <p className="hint">Loading subscriptions...</p> : null}
          {subsMessage && <p className="error">{subsMessage}</p>}

          <ul className="subscription-list">
            {subscriptions.map((subscription) => (
              <li
                key={subscription.id}
                className={selectedSubscriptionId === subscription.id ? "selected" : ""}
                onClick={() => setSelectedSubscriptionId(subscription.id)}
              >
                <div>
                  <strong>{subscription.type}</strong>
                  <p>{maskTarget(subscription.type, subscription.target)}</p>
                  <p>
                    scope: {subscription.scopeType} • metric: {subscription.metric ?? "*"}
                  </p>
                  <p>
                    {subscription.scopeType === "region"
                      ? `region=${subscription.region}`
                      : `institution=${institutionsById.get(subscription.institutionId ?? "")?.name ?? subscription.institutionId}`}
                  </p>
                </div>
                <button type="button" onClick={() => onDisableSubscription(subscription.id)}>
                  Disable
                </button>
              </li>
            ))}
          </ul>

          {subscriptions.length === 0 && !loadingSubscriptions ? (
            <p className="hint">No enabled subscriptions for this source.</p>
          ) : null}

          <div className="health">
            <div className="panel-header">
              <h2>Delivery Health</h2>
              <button
                type="button"
                onClick={() => selectedSubscriptionId && loadDeliveries(selectedSubscriptionId, session.accessToken)}
                disabled={!selectedSubscriptionId || loadingDeliveries}
              >
                Refresh
              </button>
            </div>

            {selectedSubscription ? <p className="hint">Subscription: {selectedSubscription.id}</p> : null}
            {loadingDeliveries ? <p className="hint">Loading deliveries...</p> : null}

            <table>
              <thead>
                <tr>
                  <th>Status</th>
                  <th>Attempts</th>
                  <th>Created</th>
                  <th>Sent</th>
                  <th>Last error</th>
                </tr>
              </thead>
              <tbody>
                {deliveries.map((delivery) => (
                  <tr key={`${delivery.eventId}:${delivery.createdAtUtc}`}>
                    <td>{delivery.status}</td>
                    <td>{delivery.attemptCount}</td>
                    <td>{formatUtc(delivery.createdAtUtc)}</td>
                    <td>{delivery.sentAtUtc ? formatUtc(delivery.sentAtUtc) : "-"}</td>
                    <td title={delivery.lastError ?? ""}>{delivery.lastError ?? "-"}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            {deliveries.length === 0 && !loadingDeliveries ? <p className="hint">No deliveries yet.</p> : null}
          </div>
        </section>
      </main>
    </div>
  );
}

type CopilotPageProps = {
  onLogout: (message?: string) => void;
  onAuthExpired: () => void;
};

function CopilotPage({ onLogout, onAuthExpired }: CopilotPageProps) {
  const navigate = useNavigate();
  const [sources, setSources] = useState<SourceItem[]>([]);
  const [selectedSource, setSelectedSource] = useState("");
  const [adminApiKey, setAdminApiKey] = useState("");
  const [question, setQuestion] = useState("What is critical now and where?");
  const [busyAsk, setBusyAsk] = useState(false);
  const [busyBriefing, setBusyBriefing] = useState<null | "daily" | "weekly">(null);
  const [busyFeatureFlagAction, setBusyFeatureFlagAction] = useState<null | "status" | "toggle">(null);
  const [message, setMessage] = useState<string | null>(null);
  const [answer, setAnswer] = useState<CopilotAnswer | null>(null);
  const [windowLabel, setWindowLabel] = useState<string | null>(null);
  const [featureFlagStatus, setFeatureFlagStatus] = useState<CopilotFeatureFlagResponse | null>(null);

  useEffect(() => {
    void loadSources();
  }, []);

  async function loadSources() {
    try {
      const payload = await requestJson<{ items: SourceItem[] }>("/api/v1/sources");
      setSources(payload.items);
      if (payload.items.length > 0) {
        setSelectedSource(payload.items[0].source);
      }
    } catch (error) {
      if (isAuthError(error)) {
        onAuthExpired();
      } else {
        setMessage(readError(error));
      }
    }
  }

  function getAdminHeader(showRequiredMessage = true): HeadersInit | null {
    const normalized = adminApiKey.trim();
    if (normalized.length === 0) {
      if (showRequiredMessage) {
        setMessage("Admin API key is required.");
      }
      return null;
    }

    return { "X-Admin-Api-Key": normalized };
  }

  async function askCopilot() {
    const adminHeader = getAdminHeader();
    if (!adminHeader) {
      return;
    }

    const normalizedQuestion = question.trim();
    if (normalizedQuestion.length === 0) {
      setMessage("Question is required.");
      return;
    }

    setBusyAsk(true);
    setMessage(null);
    setWindowLabel(null);
    try {
      const payload = await requestJson<CopilotAnswer>("/api/v1/copilot/ask", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...adminHeader
        },
        body: JSON.stringify({
          question: normalizedQuestion,
          source: selectedSource || undefined
        })
      });
      setAnswer(payload);
    } catch (error) {
      setAnswer(null);
      setMessage(readError(error));
    } finally {
      setBusyAsk(false);
    }
  }

  async function loadBriefing(type: "daily" | "weekly") {
    const adminHeader = getAdminHeader();
    if (!adminHeader) {
      return;
    }

    setBusyBriefing(type);
    setMessage(null);
    try {
      const payload = await requestJson<CopilotBriefingResponse>(`/api/v1/copilot/briefing/${type}`, {
        headers: adminHeader
      });
      setAnswer(payload.answer);
      setWindowLabel(
        `${payload.briefingType.toUpperCase()} window: ${formatUtc(payload.windowStartUtc)} -> ${formatUtc(payload.windowEndUtc)}`
      );
    } catch (error) {
      setAnswer(null);
      setWindowLabel(null);
      setMessage(readError(error));
    } finally {
      setBusyBriefing(null);
    }
  }

  async function loadFeatureFlagStatus(showRequiredMessage = false) {
    const adminHeader = getAdminHeader(showRequiredMessage);
    if (!adminHeader) {
      return;
    }

    setBusyFeatureFlagAction("status");
    setMessage(null);
    try {
      const payload = await requestJson<CopilotFeatureFlagResponse>("/api/v1/copilot/status", {
        headers: adminHeader
      });
      setFeatureFlagStatus(payload);
    } catch (error) {
      setMessage(readError(error));
    } finally {
      setBusyFeatureFlagAction(null);
    }
  }

  async function setFeatureFlag(enabled: boolean) {
    const adminHeader = getAdminHeader();
    if (!adminHeader) {
      return;
    }

    setBusyFeatureFlagAction("toggle");
    setMessage(null);
    try {
      const payload = await requestJson<CopilotFeatureFlagResponse>("/api/v1/copilot/feature-flag", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...adminHeader
        },
        body: JSON.stringify({ enabled })
      });
      setFeatureFlagStatus(payload);
      setMessage(enabled ? "Copilot infrastructure enabled." : "Copilot infrastructure disabled.");
    } catch (error) {
      setMessage(readError(error));
    } finally {
      setBusyFeatureFlagAction(null);
    }
  }

  return (
    <div className="page">
      <header className="hero">
        <div className="hero-row">
          <div>
            <p className="kicker">BloodWatch Console</p>
            <h1>Copilot Internal</h1>
            <p>
              Internal read-only Copilot with guardrails. Requires <code>X-Admin-Api-Key</code> and returns structured
              answers with citations.
            </p>
          </div>
          <div className="hero-actions">
            <section className="copilot-control-card">
              <p className="kicker">Copilot Infra</p>
              <div className="copilot-control-header">
                <span
                  className={`copilot-status-pill ${
                    featureFlagStatus ? (featureFlagStatus.enabled ? "enabled" : "disabled") : "unknown"
                  }`}
                >
                  {featureFlagStatus ? (featureFlagStatus.enabled ? "Enabled" : "Disabled") : "Unknown"}
                </span>
                <button
                  type="button"
                  onClick={() => void loadFeatureFlagStatus(true)}
                  disabled={busyFeatureFlagAction !== null || busyAsk || busyBriefing !== null}
                >
                  {busyFeatureFlagAction === "status" ? "Checking..." : "Refresh"}
                </button>
              </div>
              <label className="copilot-toggle-row">
                <span>{busyFeatureFlagAction === "toggle" ? "Applying..." : "Enable / Disable"}</span>
                <span className="copilot-toggle">
                  <input
                    type="checkbox"
                    checked={featureFlagStatus?.enabled ?? false}
                    disabled={busyFeatureFlagAction !== null || busyAsk || busyBriefing !== null}
                    onChange={(event) => {
                      void setFeatureFlag(event.target.checked);
                    }}
                  />
                  <span className="copilot-toggle-slider" />
                </span>
              </label>
              <p className="hint">
                {featureFlagStatus
                  ? `Status updated: ${formatUtc(featureFlagStatus.updatedAtUtc)}`
                  : "Type admin key and click Refresh."}
              </p>
            </section>

            <div className="hero-actions-buttons">
              <button type="button" onClick={() => navigate("/subscriptions")}>Back to Subscriptions</button>
              <button type="button" onClick={() => onLogout("Session cleared.")}>Sign out</button>
            </div>
          </div>
        </div>
      </header>

      <main className="grid">
        <section className="panel">
          <h2>Ask Copilot</h2>
          <form
            className="stack"
            onSubmit={(event) => {
              event.preventDefault();
              void askCopilot();
            }}
          >
            <label>
              Source
              <select value={selectedSource} onChange={(event) => setSelectedSource(event.target.value)}>
                {sources.map((source) => (
                  <option key={source.source} value={source.source}>
                    {source.name} ({source.source})
                  </option>
                ))}
              </select>
            </label>

            <label>
              Admin API key
              <input
                type="password"
                value={adminApiKey}
                onChange={(event) => setAdminApiKey(event.target.value)}
                onBlur={() => {
                  if (adminApiKey.trim().length > 0) {
                    void loadFeatureFlagStatus();
                  }
                }}
                placeholder="X-Admin-Api-Key"
                autoComplete="off"
                required
              />
            </label>

            <label>
              Question
              <textarea
                value={question}
                onChange={(event) => setQuestion(event.target.value)}
                rows={5}
                placeholder="What changed since last week?"
              />
            </label>

            <div className="actions">
              <button type="submit" disabled={busyAsk || busyBriefing !== null || busyFeatureFlagAction !== null}>
                {busyAsk ? "Asking..." : "Ask"}
              </button>
              <button
                type="button"
                onClick={() => void loadBriefing("daily")}
                disabled={busyAsk || busyBriefing !== null || busyFeatureFlagAction !== null}
              >
                {busyBriefing === "daily" ? "Loading..." : "Daily Briefing"}
              </button>
              <button
                type="button"
                onClick={() => void loadBriefing("weekly")}
                disabled={busyAsk || busyBriefing !== null || busyFeatureFlagAction !== null}
              >
                {busyBriefing === "weekly" ? "Loading..." : "Weekly Briefing"}
              </button>
            </div>
          </form>

          <p className="hint">
            Use the top toggle to hard-toggle Ollama (real memory savings) during controlled windows on low-memory hosts.
          </p>
          {message ? <p className="error">{message}</p> : null}
        </section>

        <section className="panel">
          <h2>Result</h2>
          {!answer ? (
            <p className="hint">No result yet. Ask a question or run a briefing.</p>
          ) : (
            <div className="stack">
              <p><strong>{answer.shortAnswer}</strong></p>

              {answer.summaryBullets.length > 0 ? (
                <ul className="copilot-list">
                  {answer.summaryBullets.map((item, index) => (
                    <li key={`${index}-${item}`}>{item}</li>
                  ))}
                </ul>
              ) : null}

              {windowLabel ? <p className="hint">{windowLabel}</p> : null}
              <p className="hint">Model: {answer.model} • Generated: {formatUtc(answer.generatedAtUtc)}</p>
              <p className="hint">{answer.disclaimer}</p>

              <h3>Data Basis</h3>
              <ul className="copilot-list">
                {answer.dataBasis.map((entry) => (
                  <li key={entry.queryId}>
                    <code>{entry.queryId}</code> — {entry.description}
                  </li>
                ))}
              </ul>

              <h3>Citations</h3>
              <ul className="copilot-list">
                {answer.citations.map((entry) => (
                  <li key={entry.queryId}>
                    <code>{entry.queryId}</code> — {entry.resultIds.length > 0 ? entry.resultIds.join(", ") : "(none)"}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}

async function requestJson<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...options,
    headers: {
      Accept: "application/json",
      ...(options?.headers ?? {})
    }
  });

  if (!response.ok) {
    throw await readApiError(response);
  }

  return (await response.json()) as T;
}

async function requestEmpty(path: string, options?: RequestInit): Promise<void> {
  const response = await fetch(path, {
    ...options,
    headers: {
      Accept: "application/json",
      ...(options?.headers ?? {})
    }
  });

  if (!response.ok) {
    throw await readApiError(response);
  }
}

async function readApiError(response: Response): Promise<ApiError> {
  const contentType = response.headers.get("content-type") ?? "";

  if (contentType.includes("application/json")) {
    const body = (await response.json()) as ApiError;
    return {
      status: response.status,
      title: body.title,
      detail: body.detail
    };
  }

  return {
    status: response.status,
    detail: await response.text()
  };
}

function withBearer(accessToken: string): HeadersInit {
  return {
    Authorization: `Bearer ${accessToken}`
  };
}

function isAuthError(error: unknown): error is ApiError {
  return typeof error === "object" && error !== null && (error as ApiError).status === 401;
}

function readError(error: unknown): string {
  if (typeof error === "object" && error !== null) {
    const typedError = error as ApiError;
    if (typedError.detail) {
      return typedError.detail;
    }

    if (typedError.title) {
      return typedError.title;
    }

    if (typedError.status) {
      return `Request failed (${typedError.status}).`;
    }
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "Unexpected request failure.";
}

function maskTarget(type: string, value: string): string {
  if (type === "telegram:chat") {
    const normalized = value.trim();
    return `***${normalized.length <= 4 ? normalized : normalized.slice(-4)}`;
  }

  if (value.includes("***")) {
    return value;
  }

  if (type === "discord:webhook") {
    return `${value.slice(0, 36)}...${value.slice(-6)}`;
  }

  return "***";
}

function formatUtc(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toISOString().replace("T", " ").replace(".000Z", " UTC");
}
