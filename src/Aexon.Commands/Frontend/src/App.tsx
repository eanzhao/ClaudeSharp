import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  ChevronLeft,
  Database,
  Globe,
  Loader2,
  LogOut,
  Moon,
  Palette,
  Settings,
  Shield,
  Sun,
  Wrench,
} from 'lucide-react';
import * as api from './api';
import * as nyxid from './auth/nyxid';
import ScopePage from './runtime/ScopePage';
import './index.css';

type WorkspacePage = 'console' | 'settings';
type NonSettingsWorkspacePage = Exclude<WorkspacePage, 'settings'>;
type SettingsSection = 'runtime' | 'cloud-config' | 'skills' | 'appearance';
type AppHostMode = 'embedded' | 'proxy';

const WORKSPACE_PAGE_VALUES: WorkspacePage[] = ['console', 'settings'];
const NON_SETTINGS_WORKSPACE_PAGE_VALUES: NonSettingsWorkspacePage[] = ['console'];

type AppContextState = {
  hostMode: AppHostMode;
  scopeId: string | null;
  scopeResolved: boolean;
  scopeSource: string;
};

type AuthSessionState = {
  loading: boolean;
  enabled: boolean;
  authenticated: boolean;
  providerDisplayName: string;
  loginUrl: string;
  logoutUrl: string;
  name: string;
  email: string;
  picture: string;
  errorMessage: string;
};

type ProviderTypeOption = {
  id: string;
  displayName: string;
  category: string;
  description: string;
  recommended: boolean;
  defaultEndpoint: string;
  defaultModel: string;
};

type ProviderDraft = {
  key: string;
  providerName: string;
  providerType: string;
  displayName: string;
  category: string;
  description: string;
  endpoint: string;
  model: string;
  apiKey: string;
  apiKeyConfigured: boolean;
};

type RuntimeMode = 'remote' | 'local';
type UserLlmRoute = string;

type UserConfigProviderStatus = {
  provider_slug: string;
  provider_name: string;
  status: string;
  source?: string;
};

type UserConfigState = {
  defaultModel: string;
  preferredLlmRoute: UserLlmRoute;
  loading: boolean;
  providers: UserConfigProviderStatus[];
  supportedModels: string[];
  modelsByProvider: Record<string, string[]>;
  modelsLoading: boolean;
};

type StudioSettingsState = {
  remoteRuntimeUrl: string;
  localRuntimeUrl: string;
  runtimeMode: RuntimeMode;
  ornnBaseUrl: string;
  appearanceTheme: string;
  colorMode: 'light' | 'dark';
  secretsFilePath: string;
  defaultProviderName: string;
  providerTypes: ProviderTypeOption[];
  providers: ProviderDraft[];
};

type AppearanceOption = {
  id: string;
  label: string;
  description: string;
  swatches: string[];
};

const APPEARANCE_OPTIONS: AppearanceOption[] = [
  {
    id: 'blue',
    label: 'Blue',
    description: 'Cool and calm for daily editing.',
    swatches: ['#2F6FEC', '#7FA9FF', '#EAF2FF'],
  },
  {
    id: 'coral',
    label: 'Coral',
    description: 'The warmer look you had before.',
    swatches: ['#F0483E', '#FF8A6B', '#FFF1EC'],
  },
  {
    id: 'forest',
    label: 'Forest',
    description: 'A softer green studio palette.',
    swatches: ['#2F8F6A', '#6BBC94', '#EAF7F0'],
  },
];

const DEFAULT_REMOTE_RUNTIME_URL = 'https://aevatar-console-backend-api.aevatar.ai';
const DEFAULT_LOCAL_RUNTIME_URL = 'http://127.0.0.1:5080';
const DEFAULT_RUNTIME_BASE_URL = DEFAULT_LOCAL_RUNTIME_URL;
const USER_LLM_ROUTE_GATEWAY = '';
const USER_CONFIG_PROVIDER_SOURCE_GATEWAY = 'gateway_provider';
const USER_CONFIG_PROVIDER_SOURCE_SERVICE = 'user_service';
const WORKSPACE_PAGE_STORAGE_KEY = 'aevatar.app.workspace-page';
const PREVIOUS_WORKSPACE_PAGE_STORAGE_KEY = 'aevatar.app.previous-workspace-page';
const APPEARANCE_THEME_STORAGE_KEY = 'aevatar.app.appearance-theme';
const COLOR_MODE_STORAGE_KEY = 'aevatar.app.color-mode';

function normalizeRuntimeMode(value: unknown): RuntimeMode {
  return String(value || '').trim().toLowerCase() === 'remote' ? 'remote' : 'local';
}

function normalizeRuntimeUrl(value: unknown, fallback: string) {
  const normalized = String(value || '').trim();
  return (normalized || fallback).replace(/\/+$/, '');
}

function normalizeUserLlmRoute(value: unknown): UserLlmRoute {
  const normalized = String(value || '').trim();
  if (!normalized || /^auto$/i.test(normalized) || /^gateway$/i.test(normalized)) {
    return USER_LLM_ROUTE_GATEWAY;
  }

  if (normalized.includes('://') || normalized.startsWith('//')) {
    return USER_LLM_ROUTE_GATEWAY;
  }

  if (normalized.startsWith('/')) {
    return normalized;
  }

  return `/api/v1/proxy/s/${normalized.replace(/^\/+|\/+$/g, '')}`;
}

function routePathFromProviderSlug(slug: string) {
  const normalized = String(slug || '').trim();
  return normalized ? `/api/v1/proxy/s/${normalized}` : USER_LLM_ROUTE_GATEWAY;
}

function resolveUserRuntimeConfig(userConfigData?: any) {
  const legacyRuntimeUrl = String(userConfigData?.runtimeBaseUrl || '').trim().replace(/\/+$/, '');
  const hasExplicitRuntimeConfig = Boolean(
    userConfigData?.runtimeMode || userConfigData?.localRuntimeBaseUrl || userConfigData?.remoteRuntimeBaseUrl,
  );
  if (!hasExplicitRuntimeConfig && legacyRuntimeUrl) {
    const isLocalRuntime = /^https?:\/\/(localhost|127\.0\.0\.1|0\.0\.0\.0)(:\d+)?/i.test(legacyRuntimeUrl);
    return {
      runtimeMode: isLocalRuntime ? 'local' as const : 'remote' as const,
      localRuntimeUrl: isLocalRuntime ? legacyRuntimeUrl : DEFAULT_LOCAL_RUNTIME_URL,
      remoteRuntimeUrl: isLocalRuntime ? DEFAULT_REMOTE_RUNTIME_URL : legacyRuntimeUrl,
      activeRuntimeUrl: legacyRuntimeUrl,
    };
  }

  const runtimeMode = normalizeRuntimeMode(userConfigData?.runtimeMode);
  const localRuntimeUrl = normalizeRuntimeUrl(userConfigData?.localRuntimeBaseUrl, DEFAULT_LOCAL_RUNTIME_URL);
  const remoteRuntimeUrl = normalizeRuntimeUrl(userConfigData?.remoteRuntimeBaseUrl, DEFAULT_REMOTE_RUNTIME_URL);
  return {
    runtimeMode,
    localRuntimeUrl,
    remoteRuntimeUrl,
    activeRuntimeUrl: runtimeMode === 'remote' ? remoteRuntimeUrl : localRuntimeUrl,
  };
}

function getActiveRuntimeUrl(runtime: Pick<StudioSettingsState, 'runtimeMode' | 'localRuntimeUrl' | 'remoteRuntimeUrl'>) {
  return runtime.runtimeMode === 'remote'
    ? normalizeRuntimeUrl(runtime.remoteRuntimeUrl, DEFAULT_REMOTE_RUNTIME_URL)
    : normalizeRuntimeUrl(runtime.localRuntimeUrl, DEFAULT_LOCAL_RUNTIME_URL);
}

function createEmptyAppContext(): AppContextState {
  return {
    hostMode: 'embedded',
    scopeId: null,
    scopeResolved: false,
    scopeSource: '',
  };
}

function resolveAppContextState(context: any): AppContextState {
  const resolvedScopeId = context?.scopeResolved && context?.scopeId ? context.scopeId : null;
  return {
    hostMode: context?.mode === 'proxy' ? 'proxy' : 'embedded',
    scopeId: resolvedScopeId,
    scopeResolved: Boolean(resolvedScopeId),
    scopeSource: context?.scopeSource || '',
  };
}

function isAuthResponseInvalid(error: any) {
  return Boolean(
    error?.status === 401 ||
    error?.loginUrl ||
    (typeof error?.message === 'string' && error.message.includes('Sign-in may be required.')) ||
    (typeof error?.rawBody === 'string' &&
      (error.rawBody.startsWith('<!DOCTYPE') || error.rawBody.startsWith('<html'))),
  );
}

function summarizeBootstrapFailures(labels: string[]) {
  if (labels.length === 0) {
    return '';
  }

  const visibleLabels = labels.slice(0, 3);
  const suffix = labels.length > visibleLabels.length
    ? `, +${labels.length - visibleLabels.length} more`
    : '';
  return `Loaded studio with defaults for ${visibleLabels.join(', ')}${suffix}.`;
}

function summarizeChronoStorageWarning(failures: Array<{ label: string; error: any }>) {
  if (failures.length === 0) {
    return null;
  }

  const labels = Array.from(new Set(failures.map(item => item.label)));
  const affectedLabel = labels.length === 1
    ? labels[0]
    : `${labels.slice(0, 2).join(', ')}${labels.length > 2 ? `, +${labels.length - 2} more` : ''}`;
  return `${api.getChronoStorageServiceErrorMessage(failures[0].error)} Affected: ${affectedLabel}.`;
}

function isWorkspacePage(value: string | null): value is WorkspacePage {
  return Boolean(value && WORKSPACE_PAGE_VALUES.includes(value as WorkspacePage));
}

function isNonSettingsWorkspacePage(value: string | null): value is NonSettingsWorkspacePage {
  return Boolean(value && NON_SETTINGS_WORKSPACE_PAGE_VALUES.includes(value as NonSettingsWorkspacePage));
}

function readStoredWorkspacePage(): WorkspacePage {
  if (typeof window === 'undefined') {
    return 'console';
  }

  try {
    const raw = window.localStorage.getItem(WORKSPACE_PAGE_STORAGE_KEY);
    return isWorkspacePage(raw) ? raw : 'console';
  } catch {
    return 'console';
  }
}

function readStoredPreviousWorkspacePage(): NonSettingsWorkspacePage {
  if (typeof window === 'undefined') {
    return 'console';
  }

  try {
    const raw = window.localStorage.getItem(PREVIOUS_WORKSPACE_PAGE_STORAGE_KEY);
    return isNonSettingsWorkspacePage(raw) ? raw : 'console';
  } catch {
    return 'console';
  }
}

function readStoredAppearanceTheme() {
  if (typeof window === 'undefined') {
    return 'blue';
  }

  try {
    const raw = window.localStorage.getItem(APPEARANCE_THEME_STORAGE_KEY);
    return APPEARANCE_OPTIONS.some(option => option.id === raw) ? raw! : 'blue';
  } catch {
    return 'blue';
  }
}

function readStoredColorMode(): 'light' | 'dark' {
  if (typeof window === 'undefined') {
    return 'light';
  }

  try {
    return window.localStorage.getItem(COLOR_MODE_STORAGE_KEY) === 'dark' ? 'dark' : 'light';
  } catch {
    return 'light';
  }
}

function AevatarBrandMark(props: {
  size?: number;
  className?: string;
}) {
  const size = props.size ?? 44;
  return (
    <svg
      viewBox="0 0 400 400"
      width={size}
      height={size}
      className={props.className}
      aria-hidden="true"
      shapeRendering="crispEdges"
    >
      <rect width="400" height="400" rx="28" fill="#18181B" />

      <rect x="12" y="20" width="134" height="46" fill="#FAFAFA" />
      <rect x="102" y="20" width="44" height="142" fill="#FAFAFA" />
      <rect x="0" y="66" width="70" height="30" fill="#FAFAFA" />

      <rect x="254" y="20" width="134" height="46" fill="#FAFAFA" />
      <rect x="254" y="20" width="44" height="142" fill="#FAFAFA" />
      <rect x="330" y="66" width="70" height="30" fill="#FAFAFA" />

      <rect x="0" y="181" width="170" height="32" fill="#FAFAFA" />
      <rect x="230" y="181" width="170" height="32" fill="#FAFAFA" />
      <rect x="180" y="181" width="40" height="40" fill="#FAFAFA" />

      <rect x="12" y="304" width="134" height="46" fill="#FAFAFA" />
      <rect x="102" y="242" width="44" height="109" fill="#FAFAFA" />
      <rect x="0" y="274" width="70" height="30" fill="#FAFAFA" />

      <rect x="254" y="304" width="134" height="46" fill="#FAFAFA" />
      <rect x="254" y="242" width="44" height="109" fill="#FAFAFA" />
      <rect x="330" y="274" width="70" height="30" fill="#FAFAFA" />
    </svg>
  );
}

function createEmptyAuthSession(): AuthSessionState {
  return {
    loading: true,
    enabled: true,
    authenticated: false,
    providerDisplayName: 'NyxID',
    loginUrl: '/auth/login',
    logoutUrl: '/auth/logout',
    name: '',
    email: '',
    picture: '',
    errorMessage: '',
  };
}

const DEFAULT_ORNN_BASE_URL = 'https://ornn.chrono-ai.fun';

function createEmptyStudioSettings(): StudioSettingsState {
  return {
    remoteRuntimeUrl: DEFAULT_REMOTE_RUNTIME_URL,
    localRuntimeUrl: DEFAULT_LOCAL_RUNTIME_URL,
    runtimeMode: 'local',
    ornnBaseUrl: DEFAULT_ORNN_BASE_URL,
    appearanceTheme: readStoredAppearanceTheme(),
    colorMode: readStoredColorMode(),
    secretsFilePath: '',
    defaultProviderName: '',
    providerTypes: [],
    providers: [],
  };
}

function getFixedProviderName(providerType: string) {
  return providerType.trim().toLowerCase() === 'nyxid' ? 'nyxid' : '';
}

function usesFixedProviderName(providerType: string) {
  return Boolean(getFixedProviderName(providerType));
}

function toProviderDraft(item: any, providerTypes: ProviderTypeOption[]): ProviderDraft {
  const profile = providerTypes.find(option => option.id === item?.providerType) || null;
  return {
    key: `provider_${crypto.randomUUID()}`,
    providerName: item?.providerName || '',
    providerType: item?.providerType || profile?.id || 'openai',
    displayName: item?.displayName || profile?.displayName || item?.providerType || 'Provider',
    category: item?.category || profile?.category || 'configured',
    description: item?.description || profile?.description || '',
    endpoint: item?.endpoint || profile?.defaultEndpoint || '',
    model: item?.model || profile?.defaultModel || '',
    apiKey: item?.apiKey || '',
    apiKeyConfigured: Boolean(item?.apiKeyConfigured),
  };
}

function App() {
  const [, setAppContext] = useState<AppContextState>(createEmptyAppContext());
  const [authSession, setAuthSession] = useState<AuthSessionState>(createEmptyAuthSession());
  const [workspacePage, setWorkspacePage] = useState<WorkspacePage>(() => readStoredWorkspacePage());
  const [previousWorkspacePage, setPreviousWorkspacePage] = useState<NonSettingsWorkspacePage>(() => readStoredPreviousWorkspacePage());
  const [settingsSection, setSettingsSection] = useState<SettingsSection>('runtime');
  const [userConfigState, setUserConfigState] = useState<UserConfigState>({
    defaultModel: '',
    preferredLlmRoute: USER_LLM_ROUTE_GATEWAY,
    loading: false,
    providers: [],
    supportedModels: [],
    modelsByProvider: {},
    modelsLoading: false,
  });

  const [, setWorkspaceSettings] = useState<{
    runtimeBaseUrl: string;
  }>({
    runtimeBaseUrl: DEFAULT_RUNTIME_BASE_URL,
  });

  const [settingsState, setSettingsState] = useState<StudioSettingsState>(createEmptyStudioSettings());
  const [, setSelectedProviderKey] = useState<string | null>(null);


  const [ornnTestState, setOrnnTestState] = useState<{ status: 'idle' | 'testing' | 'success' | 'error'; message: string }>({ status: 'idle', message: '' });
  const [ornnSkillsCache, setOrnnSkillsCache] = useState<api.OrnnSkillSummary[]>([]);
  const [ornnSkillsLoading, setOrnnSkillsLoading] = useState(false);
  const [runtimeTestState, setRuntimeTestState] = useState<{
    status: 'idle' | 'testing' | 'success' | 'error';
    message: string;
  }>({
    status: 'idle',
    message: '',
  });

  const [statusMessage, setStatusMessage] = useState<{
    text: string;
    type: 'success' | 'error' | 'info';
  } | null>(null);
  const [, setStorageWarning] = useState<string | null>(null);

  const toastTimerRef = useRef<number | null>(null);

  useEffect(() => {
    void bootstrap();

    // In Electron, listen for OAuth callback from the loopback server
    const cleanup = nyxid.setupElectronAuthListener(
      ({ session: oauthSession }) => {
        setAuthSession({
          loading: false,
          enabled: true,
          authenticated: true,
          providerDisplayName: 'NyxID',
          loginUrl: '',
          logoutUrl: '',
          name: oauthSession.user.name || '',
          email: oauthSession.user.email || '',
          picture: oauthSession.user.picture || '',
          errorMessage: '',
        });
        // Re-bootstrap to load workspace data now that we're authenticated
        void bootstrap();
      },
      (err) => {
        setAuthSession(prev => ({
          ...prev,
          loading: false,
          enabled: true,
          authenticated: false,
          errorMessage: err?.message || 'OAuth callback failed.',
        }));
      },
    );
    return cleanup || undefined;
  }, []);

  useEffect(() => api.onAuthRequired(detail => {
    setAuthSession(prev => ({
      ...prev,
      loading: false,
      enabled: true,
      authenticated: false,
      loginUrl: detail?.loginUrl || prev.loginUrl || '/auth/login',
      errorMessage: detail?.message || 'Sign in to continue.',
    }));
  }), []);

  useEffect(() => {
    if (typeof window === 'undefined') {
      return;
    }

    try {
      window.localStorage.setItem(WORKSPACE_PAGE_STORAGE_KEY, workspacePage);
    } catch {
      // Ignore storage errors in restricted browser contexts.
    }
  }, [workspacePage]);

  useEffect(() => {
    if (typeof window === 'undefined') {
      return;
    }

    try {
      window.localStorage.setItem(PREVIOUS_WORKSPACE_PAGE_STORAGE_KEY, previousWorkspacePage);
    } catch {
      // Ignore storage errors in restricted browser contexts.
    }
  }, [previousWorkspacePage]);

  useEffect(() => {
    if (typeof window === 'undefined') {
      return;
    }

    try {
      window.localStorage.setItem(APPEARANCE_THEME_STORAGE_KEY, settingsState.appearanceTheme || 'blue');
      window.localStorage.setItem(COLOR_MODE_STORAGE_KEY, settingsState.colorMode || 'light');
    } catch {
      // Ignore storage errors in restricted browser contexts.
    }
  }, [settingsState.appearanceTheme, settingsState.colorMode]);

  useEffect(() => {
    if (typeof document === 'undefined') {
      return;
    }

    document.documentElement.style.colorScheme = settingsState.colorMode;
    document.body.style.background = settingsState.colorMode === 'dark' ? '#0b1220' : '#f7f6f3';
    document.body.style.color = settingsState.colorMode === 'dark' ? '#e5e7eb' : '#1f2328';
  }, [settingsState.colorMode]);

  useEffect(() => {
    return () => {
      if (toastTimerRef.current) {
        window.clearTimeout(toastTimerRef.current);
      }
    };
  }, []);

  async function bootstrap() {
    try {
      // Handle OAuth callback if we're returning from NyxID.
      if (nyxid.isAuthCallback()) {
        try {
          const { session: oauthSession, returnTo } = await nyxid.handleCallback();
          setAuthSession({
            loading: false,
            enabled: true,
            authenticated: true,
            providerDisplayName: 'NyxID',
            loginUrl: '',
            logoutUrl: '',
            name: oauthSession.user.name || '',
            email: oauthSession.user.email || '',
            picture: oauthSession.user.picture || '',
            errorMessage: '',
          });
          // Clean up callback URL params.
          window.history.replaceState({}, '', returnTo || '/');
        } catch (err: any) {
          setAuthSession(prev => ({
            ...prev,
            loading: false,
            enabled: true,
            authenticated: false,
            errorMessage: err?.message || 'OAuth callback failed.',
          }));
          window.history.replaceState({}, '', '/');
          return;
        }
      }

      // Check for existing NyxID session in localStorage.
      let localSession = nyxid.loadSession();
      if (localSession && !nyxid.getActiveSession()) {
        // Token expired but refresh token available.
        localSession = await nyxid.refreshSession(localSession);
      }

      // Also check backend auth status (for scope resolution).
      let backendSession: any = null;
      try {
        backendSession = await api.auth.getSession();
      } catch { /* backend may not have auth endpoint */ }

      const isAuthenticated = Boolean(localSession) || Boolean(backendSession?.authenticated);
      const nextAuthSession: AuthSessionState = {
        loading: false,
        enabled: true,
        authenticated: isAuthenticated,
        providerDisplayName: 'NyxID',
        loginUrl: '',
        logoutUrl: '',
        name: localSession?.user.name || backendSession?.name || '',
        email: localSession?.user.email || backendSession?.email || '',
        picture: localSession?.user.picture || '',
        errorMessage: '',
      };
      setAuthSession(nextAuthSession);

      if (!isAuthenticated) {
        return;
      }

      const [
        contextResult,
        settingsResult,
        userConfigResult,
      ] = await Promise.allSettled([
        api.app.getContext(),
        api.settings.get(),
        api.userConfig.get(),
      ]);

      const bootstrapFailures = [
        { label: 'app context', result: contextResult },
        { label: 'studio settings', result: settingsResult },
        { label: 'user config', result: userConfigResult },
      ].flatMap(item =>
        item.result.status === 'rejected'
          ? [{ label: item.label, error: item.result.reason }]
          : []);

      const authFailure = bootstrapFailures.find(item => isAuthResponseInvalid(item.error));
      if (authFailure) {
        setStorageWarning(null);
        setAuthSession(prev => ({
          ...prev,
          loading: false,
          enabled: true,
          authenticated: false,
          loginUrl: authFailure.error?.loginUrl || prev.loginUrl || '/auth/login',
          errorMessage: authFailure.error?.message || 'Sign in to continue.',
        }));
        return;
      }

      bootstrapFailures.forEach(item => {
        console.warn(`[Aevatar App] Failed to load bootstrap resource: ${item.label}`, item.error);
      });

      const chronoStorageFailures = bootstrapFailures.filter(item => api.isChronoStorageServiceError(item.error));
      const nonChronoStorageFailures = bootstrapFailures.filter(item => !api.isChronoStorageServiceError(item.error));
      const nextStorageWarning = summarizeChronoStorageWarning(chronoStorageFailures);
      setStorageWarning(nextStorageWarning);

      const context = contextResult.status === 'fulfilled' ? contextResult.value : null;
      const settings = settingsResult.status === 'fulfilled' ? settingsResult.value : null;
      const userConfigData = userConfigResult.status === 'fulfilled' ? userConfigResult.value : null;

      const runtimeConfig = resolveUserRuntimeConfig(userConfigData);
      const nextRuntime = runtimeConfig.activeRuntimeUrl;
      fetch('/api/_proxy/runtime-url', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ runtimeBaseUrl: nextRuntime }),
      }).catch(() => {});

      setAppContext(resolveAppContextState(context));
      setWorkspaceSettings({ runtimeBaseUrl: nextRuntime });

      hydrateSettings(settings, {
        ...runtimeConfig,
        defaultModel: userConfigData?.defaultModel || '',
        preferredLlmRoute: normalizeUserLlmRoute(userConfigData?.preferredLlmRoute),
      });
      setUserConfigState(prev => ({
        ...prev,
        defaultModel: userConfigData?.defaultModel || '',
        preferredLlmRoute: normalizeUserLlmRoute(userConfigData?.preferredLlmRoute),
        loading: false,
      }));

      if (nextStorageWarning) {
        flash(nextStorageWarning, 'error');
      }

      if (nonChronoStorageFailures.length > 0) {
        flash(summarizeBootstrapFailures(nonChronoStorageFailures.map(item => item.label)), 'info');
      }
    } catch (error: any) {
      if (isAuthResponseInvalid(error)) {
        setStorageWarning(null);
        setAuthSession(prev => ({
          ...prev,
          loading: false,
          enabled: true,
          authenticated: false,
          loginUrl: error?.loginUrl || prev.loginUrl || '/auth/login',
          errorMessage: error?.message || 'Sign in to continue.',
        }));
        return;
      }

      if (api.isChronoStorageServiceError(error)) {
        const message = api.getChronoStorageServiceErrorMessage(error);
        setStorageWarning(message);
        flash(message, 'error');
      }

      setAuthSession(prev => ({
        ...prev,
        loading: false,
        errorMessage: prev.errorMessage || error?.message || 'Failed to load app session.',
      }));
      flash(error?.message || 'Failed to load studio', 'error');
    }
  }

  function hydrateSettings(payload: any, userConfigData?: any) {
    const providerTypes = Array.isArray(payload?.providerTypes)
      ? payload.providerTypes.map((item: any) => ({
          id: item.id,
          displayName: item.displayName,
          category: item.category,
          description: item.description,
          recommended: Boolean(item.recommended),
          defaultEndpoint: item.defaultEndpoint || '',
          defaultModel: item.defaultModel || '',
        }))
      : [];

    const providers = Array.isArray(payload?.providers)
      ? payload.providers.map((item: any) => toProviderDraft(item, providerTypes))
      : [];

    // Apply user-config defaultModel to NyxID Gateway provider
    const remoteDefaultModel = userConfigData?.defaultModel;
    if (remoteDefaultModel) {
      const nyxIdProvider = providers.find((p: any) => usesFixedProviderName(p.providerType));
      if (nyxIdProvider) {
        nyxIdProvider.model = remoteDefaultModel;
      }
    }

    const runtimeConfig = resolveUserRuntimeConfig(userConfigData);

    setSettingsState({
      remoteRuntimeUrl: runtimeConfig.remoteRuntimeUrl,
      localRuntimeUrl: runtimeConfig.localRuntimeUrl,
      runtimeMode: runtimeConfig.runtimeMode,
      ornnBaseUrl: payload?.ornnBaseUrl || DEFAULT_ORNN_BASE_URL,
      appearanceTheme: payload?.appearanceTheme || 'blue',
      colorMode: payload?.colorMode === 'dark' ? 'dark' : 'light',
      secretsFilePath: payload?.secretsFilePath || '',
      defaultProviderName: payload?.defaultProviderName || '',
      providerTypes,
      providers,
    });
    setUserConfigState(prev => ({
      ...prev,
      defaultModel: String(userConfigData?.defaultModel || '').trim(),
      preferredLlmRoute: normalizeUserLlmRoute(userConfigData?.preferredLlmRoute),
    }));
    setSelectedProviderKey(providers[0]?.key || null);
    setRuntimeTestState({
      status: 'idle',
      message: '',
    });
  }

  function flash(text: string, type: 'success' | 'error' | 'info') {
    setStatusMessage({ text, type });
    if (toastTimerRef.current) {
      window.clearTimeout(toastTimerRef.current);
    }
    toastTimerRef.current = window.setTimeout(() => {
      setStatusMessage(null);
      toastTimerRef.current = null;
    }, 2600);
  }

  function openSettingsPage(section: SettingsSection = 'runtime') {
    if (workspacePage !== 'settings') {
      setPreviousWorkspacePage(workspacePage);
    }
    setWorkspacePage('settings');
    setSettingsSection(section);
  }

  function closeSettingsPage() {
    setWorkspacePage(previousWorkspacePage);
  }

  async function handleSaveSettings() {
    try {
      const nyxIdProvider = settingsState.providers.find(p => usesFixedProviderName(p.providerType));
      const runtimeConfig = {
        runtimeMode: settingsState.runtimeMode,
        localRuntimeBaseUrl: normalizeRuntimeUrl(settingsState.localRuntimeUrl, DEFAULT_LOCAL_RUNTIME_URL),
        remoteRuntimeBaseUrl: normalizeRuntimeUrl(settingsState.remoteRuntimeUrl, DEFAULT_REMOTE_RUNTIME_URL),
      };
      const activeRuntimeUrl = getActiveRuntimeUrl(settingsState);

      // Save runtime selection and defaultModel to chrono-storage (userConfig)
      // Only send defaultModel/preferredLlmRoute when non-empty to avoid clearing stored values
      // (backend treats null as "keep current" but "" as "overwrite to empty").
      const nyxIdModel = nyxIdProvider?.model.trim() || '';
      const userConfigSaveAll = api.userConfig.save({
        ...(nyxIdModel ? { defaultModel: nyxIdModel } : {}),
        ...(userConfigState.preferredLlmRoute ? { preferredLlmRoute: normalizeUserLlmRoute(userConfigState.preferredLlmRoute) } : {}),
        ...runtimeConfig,
      }).catch(() => {});

      const response = await api.settings.save({
        appearanceTheme: settingsState.appearanceTheme,
        colorMode: settingsState.colorMode,
        defaultProviderName: settingsState.defaultProviderName || null,
        providers: settingsState.providers.map(provider => ({
          providerName: provider.providerName.trim(),
          providerType: provider.providerType.trim(),
          model: provider.model.trim(),
          endpoint: provider.endpoint.trim(),
          apiKey: provider.apiKey,
        })),
      });

      await userConfigSaveAll;

      // Update the local proxy target so subsequent requests go to the new URL
      await fetch('/api/_proxy/runtime-url', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ runtimeBaseUrl: activeRuntimeUrl }),
      }).catch(() => {});

      hydrateSettings(response, {
        defaultModel: nyxIdModel,
        preferredLlmRoute: userConfigState.preferredLlmRoute,
        ...runtimeConfig,
      });
      setWorkspaceSettings(prev => ({
        ...prev,
        runtimeBaseUrl: activeRuntimeUrl,
      }));
      flash('Settings saved', 'success');
    } catch (error: any) {
      flash(error?.message || 'Failed to save settings', 'error');
    }
  }

  async function toggleColorMode() {
    const previousColorMode = settingsState.colorMode;
    const nextColorMode = settingsState.colorMode === 'dark' ? 'light' : 'dark';
    setSettingsState(prev => ({
      ...prev,
      colorMode: nextColorMode,
    }));

    try {
      await api.settings.save({
        colorMode: nextColorMode,
      });
    } catch (error: any) {
      setSettingsState(prev => ({ ...prev, colorMode: previousColorMode }));
      flash(error?.message || 'Failed to switch theme mode', 'error');
    }
  }

  async function handleTestRuntime(targetUrl?: string) {
    const urlToTest = targetUrl ?? getActiveRuntimeUrl(settingsState);
    try {
      setRuntimeTestState({
        status: 'testing',
        message: `Testing ${urlToTest} ...`,
      });

      const response = await api.settings.testRuntime({
        runtimeBaseUrl: urlToTest,
      });

      setRuntimeTestState({
        status: response?.reachable ? 'success' : 'error',
        message: response?.message || (response?.reachable ? `Connected successfully (${urlToTest}).` : `Failed to reach ${urlToTest}.`),
      });
    } catch (error: any) {
      setRuntimeTestState({
        status: 'error',
        message: error?.message || `Failed to reach ${urlToTest}.`,
      });
    }
  }

  const authProviderLabel = authSession.providerDisplayName || 'NyxID';
  const authAccountLabel = authSession.name || authSession.email || authProviderLabel;
  const authAccountSecondaryLabel = authSession.email && authSession.email !== authAccountLabel
    ? authSession.email
    : `Connected with ${authProviderLabel}`;
  const authAccountInitials = authAccountLabel
    .split(/[\s@._-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map(part => part[0]?.toUpperCase() || '')
    .join('') || 'N';
  const authGuestStatusMessage = authSession.errorMessage
    || (authSession.enabled
      ? 'Sign in to load your workspace.'
      : `${authProviderLabel} sign-in is currently unavailable.`);
  const authStatusWidget = authSession.authenticated ? (
    <div className="header-auth-chip">
      {authSession.picture ? (
        <img
          src={authSession.picture}
          alt={authAccountLabel}
          className="header-auth-avatar-image"
        />
      ) : (
        <div className="header-auth-avatar">
          {authAccountInitials}
        </div>
      )}
      <div className="header-auth-copy">
        <div className="header-auth-label">{authAccountLabel}</div>
        <div className="header-auth-meta">{authAccountSecondaryLabel}</div>
      </div>
      <button
        onClick={() => { nyxid.clearSession(); window.location.reload(); }}
        className="panel-icon-button header-auth-logout"
        title="Sign out from NyxID."
      >
        <LogOut size={14} />
      </button>
    </div>
  ) : (
    <div className="header-auth-guest">
      <div className="header-auth-guest-copy">
        <div className="header-auth-label">{authProviderLabel}</div>
        <div className="header-auth-meta">
          {authGuestStatusMessage}
        </div>
      </div>
      <button onClick={() => nyxid.loginWithRedirect(window.location.pathname)} className="solid-action header-auth-link !no-underline">
        <Shield size={14} /> Sign in
      </button>
    </div>
  );


  if (authSession.loading) {
    return (
      <AppLoadingScreen
        appearanceTheme={settingsState.appearanceTheme}
        colorMode={settingsState.colorMode}
      />
    );
  }

  if (authSession.enabled && !authSession.authenticated) {
    return (
      <AppAuthenticationGate
        providerDisplayName={authSession.providerDisplayName}
        loginUrl={authSession.loginUrl}
        errorMessage={authSession.errorMessage}
        appearanceTheme={settingsState.appearanceTheme}
        colorMode={settingsState.colorMode}
      />
    );
  }

  return (
    <div
      className="studio-shell relative flex h-screen w-full overflow-hidden bg-[#F2F1EE] text-gray-800"
      data-appearance={settingsState.appearanceTheme || 'blue'}
      data-color-mode={settingsState.colorMode || 'light'}
    >
      <div className="app-auth-anchor">
        {authStatusWidget}
      </div>

      <aside className="studio-rail">
        <div className="flex flex-col items-center gap-3">
          <div className="flex h-11 w-11 items-center justify-center overflow-hidden rounded-[14px] border border-black/10 bg-[#18181B]">
            <AevatarBrandMark size={44} />
          </div>
        </div>

        <div className="mt-auto flex flex-col items-center gap-3">
          <RailButton
            active={workspacePage === 'console'}
            label="Console"
            icon={<Globe size={18} />}
            onClick={() => setWorkspacePage('console')}
          />
          <RailButton
            active={settingsState.colorMode === 'dark'}
            label={settingsState.colorMode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
            icon={settingsState.colorMode === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
            onClick={() => { void toggleColorMode(); }}
          />
          <RailButton
            active={workspacePage === 'settings'}
            label="Settings"
            icon={<Settings size={18} />}
            onClick={() => openSettingsPage('runtime')}
          />
        </div>
      </aside>

      <main className="flex-1 min-w-0 flex flex-col">
        {workspacePage === 'console' ? (
          <ScopePage />
        ) : workspacePage === 'settings' ? (
          <section className="flex-1 min-h-0 bg-[#ECEAE6] p-6">
            <div className="h-full min-h-0 overflow-hidden rounded-[38px] border border-[#E6E3DE] bg-white/96 shadow-[0_26px_64px_rgba(17,24,39,0.08)] grid grid-cols-[260px_minmax(0,1fr)]">
              <aside className="settings-sidebar">
                <button
                  onClick={closeSettingsPage}
                  className="inline-flex items-center gap-2 text-[13px] text-gray-400 hover:text-gray-600"
                >
                  <ChevronLeft size={16} />
                  Back to workspace
                </button>

                <div className="mt-8 space-y-2">
                  <SettingsNavButton
                    active={settingsSection === 'runtime'}
                    icon={<Globe size={16} />}
                    title="Runtime"
                    description="Base URL and connectivity"
                    onClick={() => setSettingsSection('runtime')}
                  />
                  <SettingsNavButton
                    active={settingsSection === 'cloud-config'}
                    icon={<Database size={16} />}
                    title="LLM"
                    description="Per-user settings on NyxID"
                    onClick={() => setSettingsSection('cloud-config')}
                  />
                  <SettingsNavButton
                    active={settingsSection === 'skills'}
                    icon={<Wrench size={16} />}
                    title="Skills"
                    description="Ornn skill platform"
                    onClick={() => setSettingsSection('skills')}
                  />
                  <SettingsNavButton
                    active={settingsSection === 'appearance'}
                    icon={<Palette size={16} />}
                    title="Appearance"
                    description="Studio theme and accents"
                    onClick={() => setSettingsSection('appearance')}
                  />
                </div>
              </aside>

              <div className="min-h-0 overflow-y-auto px-10 py-10">
                {settingsSection === 'runtime' ? (
                  <div className="max-w-[920px] space-y-8">
                    <div>
                      <div className="panel-eyebrow">Settings</div>
                      <div className="panel-title">Runtime</div>
                    </div>

                    <div className="settings-section-card space-y-5">
                      <div>
                        <label className="field-label">Runtime Target</label>
                        <div className="mt-2 inline-flex rounded-[14px] bg-[#F2F1EE] p-1">
                          <button
                            onClick={() => {
                              setSettingsState(prev => ({ ...prev, runtimeMode: 'local' }));
                              setRuntimeTestState({ status: 'idle', message: '' });
                            }}
                            className={`px-3 py-1.5 rounded-[10px] text-[12px] font-semibold transition-colors ${
                              settingsState.runtimeMode === 'local'
                                ? 'bg-white text-gray-800 shadow-sm'
                                : 'text-gray-500 hover:text-gray-700'
                            }`}
                          >
                            Local
                          </button>
                          <button
                            onClick={() => {
                              setSettingsState(prev => ({ ...prev, runtimeMode: 'remote' }));
                              setRuntimeTestState({ status: 'idle', message: '' });
                            }}
                            className={`px-3 py-1.5 rounded-[10px] text-[12px] font-semibold transition-colors ${
                              settingsState.runtimeMode === 'remote'
                                ? 'bg-white text-gray-800 shadow-sm'
                                : 'text-gray-500 hover:text-gray-700'
                            }`}
                          >
                            Remote
                          </button>
                        </div>
                        <div className="text-[12px] text-gray-400 mt-2">
                          {settingsState.runtimeMode === 'local'
                            ? 'Default local runtime is http://127.0.0.1:5080.'
                            : 'Use a remote runtime when you need a shared or hosted backend.'}
                        </div>
                      </div>

                      <div>
                        <label className="field-label">
                          {settingsState.runtimeMode === 'local' ? 'Local Runtime URL' : 'Remote Runtime URL'}
                        </label>
                        <input
                          className="panel-input mt-1"
                          value={settingsState.runtimeMode === 'local' ? settingsState.localRuntimeUrl : settingsState.remoteRuntimeUrl}
                          onChange={event => {
                            const value = event.target.value;
                            setSettingsState(prev => prev.runtimeMode === 'local'
                              ? { ...prev, localRuntimeUrl: value }
                              : { ...prev, remoteRuntimeUrl: value });
                            setRuntimeTestState({ status: 'idle', message: '' });
                          }}
                          placeholder={settingsState.runtimeMode === 'local' ? DEFAULT_LOCAL_RUNTIME_URL : DEFAULT_REMOTE_RUNTIME_URL}
                        />
                      </div>

                      <div className="flex gap-3">
                        <button
                          onClick={() => { void handleTestRuntime(); }}
                          className="ghost-action justify-center"
                          disabled={runtimeTestState.status === 'testing'}
                        >
                          {runtimeTestState.status === 'testing' ? 'Testing...' : 'Test connection'}
                        </button>
                        <button
                          onClick={handleSaveSettings}
                          className="solid-action justify-center"
                        >
                          Save runtime
                        </button>
                      </div>

                      {runtimeTestState.status !== 'idle' ? (
                        <div className={`settings-status-card ${runtimeTestState.status}`}>
                          <div className="flex items-center justify-between gap-3">
                            <div className="text-[13px] font-semibold text-gray-800">
                              {runtimeTestState.status === 'success' ? 'Connection succeeded' : runtimeTestState.status === 'testing' ? 'Testing runtime' : 'Connection failed'}
                            </div>
                            <SettingsStatusPill status={runtimeTestState.status} />
                          </div>
                          <div className="text-[12px] text-gray-500 mt-2 break-all">
                            {getActiveRuntimeUrl(settingsState)}
                          </div>
                          <div className="text-[13px] text-gray-600 mt-3">
                            {runtimeTestState.message}
                          </div>
                        </div>
                      ) : null}
                    </div>
                  </div>
                ) : settingsSection === 'cloud-config' ? (
                  <CloudConfigSection
                    userConfigState={userConfigState}
                    setUserConfigState={setUserConfigState}
                    runtimeConfig={{
                      runtimeMode: settingsState.runtimeMode,
                      localRuntimeUrl: settingsState.localRuntimeUrl,
                      remoteRuntimeUrl: settingsState.remoteRuntimeUrl,
                    }}
                    flash={flash}
                  />
                ) : settingsSection === 'skills' ? (
                  <div className="max-w-[920px] space-y-8">
                    <div className="flex items-start justify-between gap-4">
                      <div>
                        <div className="panel-eyebrow">Settings</div>
                        <div className="panel-title">Skills</div>
                        <div className="text-[13px] text-gray-500 mt-1">Connect to your Ornn skill library. Skills are automatically available to all agents via tool calling.</div>
                      </div>
                      <button onClick={handleSaveSettings} className="solid-action">Save</button>
                    </div>
                    <div className="settings-section-card space-y-5">
                      <div className="section-heading">Ornn Platform</div>
                      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_auto_auto] lg:items-end">
                        <div>
                          <label className="field-label">Ornn Base URL</label>
                          <input className="panel-input mt-1" value={settingsState.ornnBaseUrl} onChange={e => { setSettingsState(prev => ({ ...prev, ornnBaseUrl: e.target.value })); setOrnnTestState({ status: 'idle', message: '' }); }} placeholder={DEFAULT_ORNN_BASE_URL} />
                        </div>
                        <button onClick={async () => { setOrnnTestState({ status: 'testing', message: '' }); const ok = await api.ornn.checkHealth(settingsState.ornnBaseUrl || DEFAULT_ORNN_BASE_URL); setOrnnTestState(ok ? { status: 'success', message: 'Connected to Ornn.' } : { status: 'error', message: 'Cannot reach Ornn.' }); }} className="ghost-action justify-center" disabled={ornnTestState.status === 'testing'}>{ornnTestState.status === 'testing' ? 'Testing...' : 'Test connection'}</button>
                        <a href={settingsState.ornnBaseUrl || DEFAULT_ORNN_BASE_URL} target="_blank" rel="noopener noreferrer" className="solid-action justify-center !no-underline">Open Ornn Platform</a>
                      </div>
                      {ornnTestState.status !== 'idle' ? (<div className="settings-status-card"><div className="flex items-center justify-between gap-3"><div className="text-[13px] font-semibold text-gray-800">{ornnTestState.status === 'success' ? 'Connected' : ornnTestState.status === 'testing' ? 'Testing...' : 'Failed'}</div><SettingsStatusPill status={ornnTestState.status} /></div><div className="text-[13px] text-gray-600 mt-2">{ornnTestState.message}</div></div>) : null}
                      <div className="rounded-[20px] border border-[#EEEAE4] bg-[#FAF8F4] px-4 py-3 text-[13px] text-gray-600">Agents automatically get <strong>ornn_search_skills</strong> and <strong>ornn_use_skill</strong> tools. To manage skills, use the Ornn platform.</div>
                    </div>
                    <div className="settings-section-card space-y-5">
                      <div className="flex items-center justify-between gap-4">
                        <div className="section-heading">Your Skills</div>
                        <button onClick={async () => { setOrnnSkillsLoading(true); try { const r = await api.ornn.searchSkills(settingsState.ornnBaseUrl || DEFAULT_ORNN_BASE_URL, '', 'mixed', 1, 100); setOrnnSkillsCache(r.items); } catch { flash('Failed to load skills.', 'error'); } finally { setOrnnSkillsLoading(false); } }} className="ghost-action text-[12px]" disabled={ornnSkillsLoading}>{ornnSkillsLoading ? 'Loading...' : 'Refresh'}</button>
                      </div>
                      {ornnSkillsCache.length === 0 ? (<div className="text-[13px] text-gray-400">{ornnSkillsLoading ? 'Loading...' : 'Click Refresh to load skills from Ornn.'}</div>) : (<div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">{ornnSkillsCache.map(s => (<div key={s.guid || s.name} className="rounded-[14px] border border-[#EAE4DB] bg-white p-3 space-y-1"><div className="flex items-center justify-between gap-2"><div className="text-[13px] font-semibold text-gray-800 truncate">{s.name}</div><span className="text-[10px] uppercase tracking-wide text-gray-400">{s.isPrivate ? 'private' : 'public'}</span></div><div className="text-[12px] text-gray-500 line-clamp-2">{s.description}</div></div>))}</div>)}
                    </div>
                  </div>
                ) : (
                  <div className="max-w-[920px] space-y-8">
                    <div className="flex items-start justify-between gap-4">
                      <div>
                        <div className="panel-eyebrow">Settings</div>
                        <div className="panel-title">Appearance</div>
                      </div>
                      <button onClick={handleSaveSettings} className="solid-action">
                        Save appearance
                      </button>
                    </div>

                    <div className="settings-section-card space-y-5">
                      <div className="space-y-3">
                        <div className="section-heading">Mode</div>
                        <div className="grid gap-3 md:grid-cols-2">
                          <button
                            type="button"
                            onClick={() => setSettingsState(prev => ({ ...prev, colorMode: 'light' }))}
                            className={`appearance-card ${settingsState.colorMode === 'light' ? 'active' : ''}`}
                            data-tooltip="Use the brighter studio surface."
                          >
                            <div className="flex items-center gap-3">
                              <div className="settings-mode-icon">
                                <Sun size={16} />
                              </div>
                              <div className="min-w-0 text-left">
                                <div className="text-[13px] font-semibold text-gray-800">Light</div>
                              </div>
                            </div>
                          </button>
                          <button
                            type="button"
                            onClick={() => setSettingsState(prev => ({ ...prev, colorMode: 'dark' }))}
                            className={`appearance-card ${settingsState.colorMode === 'dark' ? 'active' : ''}`}
                            data-tooltip="Use the darker studio surface."
                          >
                            <div className="flex items-center gap-3">
                              <div className="settings-mode-icon">
                                <Moon size={16} />
                              </div>
                              <div className="min-w-0 text-left">
                                <div className="text-[13px] font-semibold text-gray-800">Dark</div>
                              </div>
                            </div>
                          </button>
                        </div>
                      </div>

                      <div className="appearance-grid">
                        {APPEARANCE_OPTIONS.map(option => (
                          <button
                            key={option.id}
                            onClick={() => setSettingsState(prev => ({ ...prev, appearanceTheme: option.id }))}
                            data-tooltip={option.description}
                            className={`appearance-card ${settingsState.appearanceTheme === option.id ? 'active' : ''}`}
                          >
                            <div className="appearance-swatches">
                              {option.swatches.map(color => (
                                <span
                                  key={color}
                                  className="appearance-swatch"
                                  style={{ background: color }}
                                />
                              ))}
                            </div>
                            <div className="text-[13px] font-semibold text-gray-800">{option.label}</div>
                          </button>
                        ))}
                      </div>
                    </div>
                  </div>
                )}
              </div>
            </div>
          </section>
        ) : null}
      </main>

      {statusMessage ? (
        <div className={`absolute bottom-5 left-1/2 -translate-x-1/2 px-4 py-2 rounded-[16px] shadow-[0_18px_36px_rgba(17,24,39,0.16)] text-[13px] font-medium z-50 ${statusMessage.type === 'success' ? 'bg-green-500 text-white' : statusMessage.type === 'error' ? 'bg-red-500 text-white' : 'bg-gray-900 text-white'}`}>
          {statusMessage.text}
        </div>
      ) : null}
    </div>
  );
}

function CloudConfigSection(props: {
  userConfigState: UserConfigState;
  setUserConfigState: React.Dispatch<React.SetStateAction<UserConfigState>>;
  runtimeConfig: Pick<StudioSettingsState, 'runtimeMode' | 'localRuntimeUrl' | 'remoteRuntimeUrl'>;
  flash: (msg: string, type: 'success' | 'error') => void;
}) {
  const { userConfigState, setUserConfigState, runtimeConfig, flash } = props;
  const [filterText, setFilterText] = useState('');
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setUserConfigState(prev => ({ ...prev, modelsLoading: true }));
      try {
        const result = await api.userConfig.models();
        if (!cancelled) {
          setUserConfigState(prev => ({
            ...prev,
            providers: result?.providers ?? [],
            supportedModels: result?.supported_models ?? [],
            modelsByProvider: result?.models_by_provider ?? {},
            modelsLoading: false,
          }));
        }
      } catch {
        if (!cancelled) setUserConfigState(prev => ({ ...prev, providers: [], supportedModels: [], modelsByProvider: {}, modelsLoading: false }));
      }
    })();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (containerRef.current && e.target instanceof Node && !containerRef.current.contains(e.target)) setDropdownOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  const readyProviders = useMemo(
    () => userConfigState.providers.filter(p => p.status === 'ready'),
    [userConfigState.providers],
  );

  const gatewayProviders = useMemo(
    () => readyProviders.filter(p => (p.source || USER_CONFIG_PROVIDER_SOURCE_GATEWAY) === USER_CONFIG_PROVIDER_SOURCE_GATEWAY),
    [readyProviders],
  );

  const serviceProviders = useMemo(
    () => userConfigState.providers.filter(p => p.source === USER_CONFIG_PROVIDER_SOURCE_SERVICE),
    [userConfigState.providers],
  );

  const preferredRoute = normalizeUserLlmRoute(userConfigState.preferredLlmRoute);
  const effectiveRoute = preferredRoute;

  const routeOptions = useMemo(() => {
    const options: Array<{ value: string; label: string; note?: string }> = [
      {
        value: USER_LLM_ROUTE_GATEWAY,
        label: 'NyxID Gateway',
      },
    ];

    const seen = new Set(options.map(option => option.value));
    for (const provider of serviceProviders) {
      const slug = provider.provider_slug;
      const route = routePathFromProviderSlug(slug);
      if (!slug || seen.has(route)) continue;
      seen.add(route);
      options.push({
        value: route,
        label: provider.provider_name || slug,
        note: provider.status === 'ready' ? 'Ready' : 'Unavailable',
      });
    }

    if (!seen.has(preferredRoute) && preferredRoute !== USER_LLM_ROUTE_GATEWAY) {
      options.push({
        value: preferredRoute,
        label: preferredRoute,
        note: 'Unavailable',
      });
    }

    return options;
  }, [preferredRoute, serviceProviders]);

  const groupedModels = useMemo(() => {
    const query = filterText.trim().toLowerCase();
    const providerOrder = effectiveRoute === USER_LLM_ROUTE_GATEWAY
      ? gatewayProviders
      : userConfigState.providers.filter(provider => routePathFromProviderSlug(provider.provider_slug) === effectiveRoute);

    return providerOrder
      .map(provider => ({
        label: provider.provider_name || provider.provider_slug,
        models: (userConfigState.modelsByProvider[provider.provider_slug] || [])
          .filter(model => !query || model.toLowerCase().includes(query)),
      }))
      .filter(group => group.models.length > 0);
  }, [effectiveRoute, filterText, gatewayProviders, userConfigState.modelsByProvider, userConfigState.providers]);

  const hasData = groupedModels.length > 0;
  const effectiveRouteLabel = routeOptions.find(option => option.value === effectiveRoute)?.label
    || (effectiveRoute === USER_LLM_ROUTE_GATEWAY ? 'NyxID Gateway' : effectiveRoute);

  return (
    <div className="max-w-[920px] space-y-8">
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="panel-eyebrow">Settings</div>
          <div className="panel-title">LLM</div>
          <div className="text-[12px] text-gray-400 mt-1">Per-user configuration stored on NyxID. Changes sync across all your devices.</div>
        </div>
        <button
          onClick={async () => {
            try {
              setUserConfigState(prev => ({ ...prev, loading: true }));
              const trimmedModel = userConfigState.defaultModel.trim();
              const trimmedRoute = normalizeUserLlmRoute(userConfigState.preferredLlmRoute);
              await api.userConfig.save({
                ...(trimmedModel ? { defaultModel: trimmedModel } : {}),
                ...(trimmedRoute ? { preferredLlmRoute: trimmedRoute } : {}),
                runtimeMode: runtimeConfig.runtimeMode,
                localRuntimeBaseUrl: normalizeRuntimeUrl(runtimeConfig.localRuntimeUrl, DEFAULT_LOCAL_RUNTIME_URL),
                remoteRuntimeBaseUrl: normalizeRuntimeUrl(runtimeConfig.remoteRuntimeUrl, DEFAULT_REMOTE_RUNTIME_URL),
              });
              flash('LLM config saved', 'success');
            } catch (error: any) {
              flash(error?.message || 'Failed to save LLM config', 'error');
            } finally {
              setUserConfigState(prev => ({ ...prev, loading: false }));
            }
          }}
          disabled={userConfigState.loading}
          className="solid-action"
        >
          {userConfigState.loading ? 'Saving...' : 'Save config'}
        </button>
      </div>

      <div className="settings-section-card space-y-4">
        <div className="section-heading">Preferred route</div>
        <div>
          <label className="field-label">LLM route</label>
          <select
            className="panel-input mt-1"
            value={preferredRoute}
            onChange={event => {
              const nextRoute = normalizeUserLlmRoute(event.target.value);
              setUserConfigState(prev => ({ ...prev, preferredLlmRoute: nextRoute }));
            }}
          >
            {routeOptions.map(option => (
              <option key={option.value} value={option.value}>
                {option.note ? `${option.label} - ${option.note}` : option.label}
              </option>
            ))}
          </select>
        </div>
        <div className="text-[11px] text-gray-400 leading-relaxed">
          {preferredRoute === USER_LLM_ROUTE_GATEWAY
            ? 'All Aevatar chat requests will go through NyxID Gateway.'
            : `${effectiveRouteLabel} will be used for Aevatar chat requests via ${preferredRoute}. If it is unavailable, Aevatar falls back to NyxID Gateway.`}
        </div>
      </div>

      {/* Providers status */}
      {(userConfigState.modelsLoading || readyProviders.length > 0) && (
        <div className="settings-section-card space-y-3">
          <div className="section-heading">Connected providers</div>
          {userConfigState.modelsLoading ? (
            <div className="flex items-center gap-2 py-2 text-[12px] text-gray-400">
              <Loader2 size={16} className="animate-spin" />
              <span>Loading providers...</span>
            </div>
          ) : (
            <div className="flex flex-wrap gap-2">
              {userConfigState.providers.map(p => (
                <span
                  key={p.provider_slug}
                  className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-[12px] font-medium ${
                    p.status === 'ready'
                      ? 'bg-green-50 text-green-700'
                      : p.status === 'expired'
                      ? 'bg-amber-50 text-amber-700'
                      : 'bg-gray-100 text-gray-400'
                  }`}
                >
                  <span className={`inline-block w-1.5 h-1.5 rounded-full ${
                    p.status === 'ready' ? 'bg-green-500' : p.status === 'expired' ? 'bg-amber-500' : 'bg-gray-300'
                  }`} />
                  {p.provider_name}
                </span>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Model selection */}
      <div className="settings-section-card space-y-4">
        <div className="section-heading">Default model</div>
        <div ref={containerRef} className="relative">
          <label className="field-label">Model</label>
          {hasData ? (
            <div className="relative mt-1">
              <input
                ref={inputRef}
                className="panel-input pr-8"
                value={dropdownOpen ? filterText : userConfigState.defaultModel}
                placeholder={userConfigState.modelsLoading ? 'Loading...' : 'Select a model...'}
                onChange={e => { setFilterText(e.target.value); if (!dropdownOpen) setDropdownOpen(true); }}
                onFocus={() => { setFilterText(''); setDropdownOpen(true); }}
              />
              <button
                type="button"
                className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                onClick={() => { setDropdownOpen(!dropdownOpen); if (!dropdownOpen) { setFilterText(''); inputRef.current?.focus(); } }}
                tabIndex={-1}
              >
                <svg width="12" height="12" viewBox="0 0 12 12" fill="none"><path d="M3 5l3 3 3-3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/></svg>
              </button>
              {dropdownOpen && (
                <div className="absolute z-50 mt-1 w-full max-h-[280px] overflow-auto rounded-md border border-gray-200 bg-white shadow-lg">
                  {userConfigState.modelsLoading ? (
                    <div className="px-3 py-2 text-[12px] text-gray-400">Loading...</div>
                  ) : groupedModels.length === 0 ? (
                    <div className="px-3 py-2 text-[12px] text-gray-400">No matching models</div>
                  ) : (
                    groupedModels.map(group => (
                      <div key={group.label}>
                        <div className="px-3 pt-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-gray-400">{group.label}</div>
                        {group.models.map(model => (
                          <button
                            key={model}
                            type="button"
                            className={`w-full text-left px-3 py-1.5 text-[13px] hover:bg-gray-50 ${model === userConfigState.defaultModel ? 'bg-blue-50 text-blue-700 font-medium' : 'text-gray-700'}`}
                            onClick={() => {
                              setUserConfigState(prev => ({ ...prev, defaultModel: model }));
                              setDropdownOpen(false);
                              setFilterText('');
                            }}
                          >
                            {model}
                          </button>
                        ))}
                      </div>
                    ))
                  )}
                </div>
              )}
            </div>
          ) : (
            <input
              className="panel-input mt-1"
              value={userConfigState.defaultModel}
              placeholder={userConfigState.modelsLoading ? 'Loading...' : 'Enter model name...'}
              onChange={e => setUserConfigState(prev => ({ ...prev, defaultModel: e.target.value }))}
            />
          )}
        </div>
        <div className="text-[11px] text-gray-400 leading-relaxed">
          The default model applied to {effectiveRouteLabel}. Select from supported models, or type a model name manually.
        </div>
      </div>
    </div>
  );
}

function AppLoadingScreen(props: {
  appearanceTheme: string;
  colorMode: 'light' | 'dark';
}) {
  return (
    <div
      className="studio-shell min-h-screen bg-[#F2F1EE] text-gray-800 px-6 py-8"
      data-appearance={props.appearanceTheme || 'blue'}
      data-color-mode={props.colorMode || 'light'}
    >
      <div className="mx-auto flex min-h-[calc(100vh-4rem)] max-w-[960px] items-center justify-center">
        <div className="w-full max-w-[460px] rounded-[32px] border border-[#E6E3DE] bg-white/96 p-8 shadow-[0_28px_70px_rgba(15,23,42,0.08)]">
          <div className="panel-eyebrow">Aevatar App</div>
          <div className="mt-3 flex items-center gap-3">
            <AevatarBrandMark size={48} className="shrink-0 rounded-[18px]" />
            <div>
              <div className="text-[24px] font-semibold text-gray-900">Preparing studio</div>
              <div className="text-[13px] text-gray-500">Loading workspace context, catalogs, and runtime settings.</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function AppAuthenticationGate(props: {
  providerDisplayName: string;
  loginUrl: string;
  errorMessage: string;
  appearanceTheme: string;
  colorMode: 'light' | 'dark';
}) {
  return (
    <div
      className="studio-shell min-h-screen bg-[#F2F1EE] px-6 py-8 text-gray-800"
      data-appearance={props.appearanceTheme || 'blue'}
      data-color-mode={props.colorMode || 'light'}
    >
      <div className="mx-auto flex min-h-[calc(100vh-4rem)] max-w-[1040px] items-center justify-center">
        <div className="grid w-full max-w-[920px] gap-6 rounded-[36px] border border-[#E6E3DE] bg-white/96 p-6 shadow-[0_30px_72px_rgba(15,23,42,0.08)] md:grid-cols-[minmax(0,1.1fr)_320px] md:p-8">
          <div className="rounded-[28px] border border-[#ECE7DF] bg-[#FAF8F4] p-6 md:p-7">
            <div className="panel-eyebrow">Aevatar App</div>
            <div className="mt-3 flex items-start gap-4">
              <div className="flex h-14 w-14 flex-shrink-0 items-center justify-center rounded-[20px] bg-white text-[var(--accent,#2563eb)] shadow-[0_12px_30px_rgba(37,99,235,0.08)]">
                <Shield size={22} />
              </div>
              <div>
                <div className="text-[28px] font-semibold leading-tight text-gray-900">Sign in to open Workflow Studio</div>
                <div className="mt-3 max-w-[520px] text-[14px] leading-6 text-gray-500">
                  Studio content, workflow execution, and scope-backed assets are only available after {props.providerDisplayName || 'NyxID'} authentication succeeds.
                </div>
              </div>
            </div>

            <div className="mt-6 rounded-[22px] border border-[#E8E2D9] bg-white px-5 py-4">
              <div className="text-[12px] font-semibold uppercase tracking-[0.14em] text-gray-400">Access policy</div>
              <div className="mt-2 text-[14px] leading-6 text-gray-600">
                When the app is not authenticated, the workflow canvas, execution panel, and runtime actions stay locked. Sign in first, then Studio will load normally.
              </div>
            </div>
          </div>

          <div className="flex flex-col justify-between rounded-[28px] border border-[#ECE7DF] bg-white p-6">
            <div>
              <div className="text-[13px] font-semibold uppercase tracking-[0.14em] text-gray-400">Authentication</div>
              <div className="mt-3 text-[22px] font-semibold text-gray-900">{props.providerDisplayName || 'NyxID'}</div>
              <div className="mt-2 text-[14px] leading-6 text-gray-500">
                Use your existing identity session to continue into the app.
              </div>
              {props.errorMessage ? (
                <div className="mt-4 rounded-[18px] border border-amber-200 bg-amber-50 px-4 py-3 text-[13px] leading-5 text-amber-800">
                  {props.errorMessage}
                </div>
              ) : null}
            </div>

            <div className="mt-6 space-y-3">
              <button
                onClick={() => nyxid.loginWithRedirect('/')}
                className="solid-action w-full justify-center !no-underline"
                title={`Sign in with ${props.providerDisplayName || 'NyxID'}.`}
              >
                <Shield size={14} /> Sign in
              </button>
              <div className="text-[12px] leading-5 text-gray-400">
                After sign-in completes, the app will return to Studio automatically.
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function RailButton(props: { active: boolean; label: string; icon: ReactNode; onClick: () => void }) {
  return (
    <button
      onClick={props.onClick}
      title={props.label}
      className={`rail-button ${props.active ? 'active' : ''}`}
    >
      {props.icon}
    </button>
  );
}

function SettingsNavButton(props: {
  active: boolean;
  icon: ReactNode;
  title: string;
  description: string;
  onClick: () => void;
}) {
  return (
    <button
      onClick={props.onClick}
      title={props.description}
      className={`settings-nav-button ${props.active ? 'active' : ''}`}
    >
      <div className="settings-nav-icon">
        {props.icon}
      </div>
      <div className="min-w-0 text-left">
        <div className="text-[14px] font-semibold text-gray-800">{props.title}</div>
      </div>
    </button>
  );
}

function SettingsStatusPill(props: { status: 'idle' | 'testing' | 'success' | 'error' }) {
  const label = props.status === 'success'
    ? 'Reachable'
    : props.status === 'testing'
      ? 'Testing'
      : props.status === 'error'
        ? 'Unreachable'
        : 'Idle';

  return (
    <span className={`settings-status-pill ${props.status}`}>
      {label}
    </span>
  );
}


export default App;
