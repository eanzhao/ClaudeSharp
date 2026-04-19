/* ─── Aevatar Chat Console – API client ─── */

import { getAccessToken } from './auth/nyxid';

const BASE = '/api';
const AUTH_REQUIRED_EVENT = 'aevatar:auth-required';
const CHRONO_STORAGE_SERVICE_ERROR_CODE = 'chrono_storage_service_unavailable';
const CHRONO_STORAGE_SERVICE_DEPENDENCY = 'chrono-storage-service';
const DEFAULT_CHRONO_STORAGE_SERVICE_MESSAGE = 'Studio could not access chrono-storage-service. It may not be enabled for this host, the service may be unavailable, or NyxID may be configured to require approval for every proxy request.';

function getAuthHeaders(): Record<string, string> {
  const token = getAccessToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

type AuthRequiredDetail = {
  loginUrl?: string | null;
  message?: string | null;
};

function isJsonContentType(contentType: string | null) {
  const value = String(contentType || '').toLowerCase();
  return value.includes('application/json') || value.includes('+json');
}

function isHtmlContentType(contentType: string | null) {
  const value = String(contentType || '').toLowerCase();
  return value.includes('text/html') || value.includes('application/xhtml+xml');
}

function notifyAuthRequired(detail: AuthRequiredDetail) {
  if (typeof window === 'undefined') {
    return;
  }

  window.dispatchEvent(new CustomEvent<AuthRequiredDetail>(AUTH_REQUIRED_EVENT, {
    detail,
  }));
}

function createRequestHeaders(opts?: RequestInit) {
  const headers = new Headers(opts?.headers);
  const isFormDataBody = typeof FormData !== 'undefined' && opts?.body instanceof FormData;
  if (!isFormDataBody && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (!headers.has('Authorization')) {
    const auth = getAuthHeaders();
    if (auth.Authorization) {
      headers.set('Authorization', auth.Authorization);
    }
  }

  return headers;
}

async function parseErrorResponse(res: Response): Promise<never> {
  const contentType = res.headers.get('content-type');
  const body = isJsonContentType(contentType)
    ? await res.json().catch(() => ({}))
    : { message: await res.text().catch(() => '') };

  if (res.status === 401 || body?.loginUrl) {
    notifyAuthRequired({
      loginUrl: body?.loginUrl,
      message: body?.message || 'Sign in to continue.',
    });
  }

  throw { status: res.status, ...body };
}

export function onAuthRequired(listener: (detail: AuthRequiredDetail) => void) {
  if (typeof window === 'undefined') {
    return () => undefined;
  }

  const handler = (event: Event) => {
    const detail = (event as CustomEvent<AuthRequiredDetail>).detail || {};
    listener(detail);
  };

  window.addEventListener(AUTH_REQUIRED_EVENT, handler as EventListener);
  return () => window.removeEventListener(AUTH_REQUIRED_EVENT, handler as EventListener);
}

async function request<T>(path: string, opts?: RequestInit): Promise<T> {
  const headers = createRequestHeaders(opts);

  const res = await fetch(`${BASE}${path}`, {
    ...opts,
    headers,
    credentials: 'include',
  });

  const contentType = res.headers.get('content-type');
  if (!res.ok) {
    return parseErrorResponse(res);
  }

  if (res.status === 204) return undefined as T;
  // Handle empty body (e.g. DELETE returning 200 with Content-Length: 0)
  const contentLength = res.headers.get('content-length');
  if (contentLength === '0' || (!contentType && contentLength === '0')) return undefined as T;
  if (isJsonContentType(contentType)) {
    return res.json();
  }
  // No Content-Type but body might be empty — try to read and return if empty
  if (!contentType) {
    const text = await res.text();
    if (!text.trim()) return undefined as T;
    try { return JSON.parse(text); } catch { /* fall through */ }
  }

  if (res.redirected) {
    notifyAuthRequired({
      loginUrl: res.url,
      message: 'Sign in to continue.',
    });
  }

  const rawBody = await res.text().catch(() => '');
  if (isHtmlContentType(contentType) || res.redirected) {
    notifyAuthRequired({
      loginUrl: res.redirected ? res.url : null,
      message: 'API returned HTML instead of JSON. Sign-in may be required.',
    });
  }

  throw {
    status: res.redirected ? 401 : res.status,
    message: isHtmlContentType(contentType)
      ? 'API returned HTML instead of JSON. Sign-in may be required.'
      : 'API returned an unexpected response format.',
    rawBody,
  };
}


export function isChronoStorageServiceError(error: any) {
  return Boolean(
    error?.code === CHRONO_STORAGE_SERVICE_ERROR_CODE ||
    error?.dependency === CHRONO_STORAGE_SERVICE_DEPENDENCY ||
    (typeof error?.message === 'string' && error.message.includes('chrono-storage-service'))
  );
}

export function getChronoStorageServiceErrorMessage(error: any) {
  if (typeof error?.message === 'string' && error.message.trim()) {
    return error.message.trim();
  }

  return DEFAULT_CHRONO_STORAGE_SERVICE_MESSAGE;
}

async function streamSse(
  path: string,
  body: unknown,
  onFrame: (frame: any) => void,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch(`${BASE}${path}`, {
    method: 'POST',
    headers: {
      Accept: 'text/event-stream',
      'Content-Type': 'application/json',
      ...getAuthHeaders(),
    },
    body: JSON.stringify(body),
    credentials: 'include',
    signal,
  });

  if (res.redirected) {
    notifyAuthRequired({
      loginUrl: res.url,
      message: 'Sign in to continue.',
    });
  }

  if (!res.ok) {
    const payload = await res.json().catch(() => ({}));
    if (res.status === 401 || payload?.loginUrl) {
      notifyAuthRequired({
        loginUrl: payload?.loginUrl || (res.redirected ? res.url : null),
        message: payload?.message || 'Sign in to continue.',
      });
    }

    throw { status: res.status, ...payload };
  }

  if (!res.body) {
    return;
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { value, done } = await reader.read();
    buffer += decoder.decode(value || new Uint8Array(), { stream: !done });

    let boundary = buffer.indexOf('\n\n');
    while (boundary >= 0) {
      const eventBlock = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);

      const data = eventBlock
        .split('\n')
        .filter(line => line.startsWith('data:'))
        .map(line => line.slice(5).trim())
        .join('\n');

      if (data && data !== '[DONE]') {
        onFrame(JSON.parse(data));
      }

      boundary = buffer.indexOf('\n\n');
    }

    if (done) {
      break;
    }
  }
}

/* ─── Workspace ─── */
export const workspace = {
  listWorkflows: () => request<any[]>('/workspace/workflows'),
};

/* ─── Settings ─── */
export const settings = {
  get:         ()          => request<any>('/settings'),
  save:        (data: any) => request<any>('/settings', { method: 'PUT', body: JSON.stringify(data) }),
  testRuntime: (data: any) => request<any>('/settings/runtime/test', { method: 'POST', body: JSON.stringify(data) }),
};

/* ─── User Config (per-user, chrono-storage backed) ─── */
export const userConfig = {
  get:    ()          => request<any>('/user-config'),
  save:   (data: any) => request<any>('/user-config', { method: 'PUT', body: JSON.stringify(data) }),
  models: ()          => request<{
    providers?: { provider_slug: string; provider_name: string; status: string; proxy_url: string; source?: string }[];
    gateway_url?: string;
    supported_models?: string[];
    models_by_provider?: Record<string, string[]>;
  }>('/user-config/models'),
};

export const auth = {
  getSession: () => request<any>('/auth/me'),
};

/* ─── Scope / Runtime APIs (remote runtime) ─── */
export const scope = {
  /** GET /api/scopes/{scopeId}/binding — read current default scope binding */
  getBinding: (scopeId: string) =>
    request<any>(`/scopes/${enc(scopeId)}/binding`),

  /** PUT /api/scopes/{scopeId}/binding — bind a static GAgent as a scope service */
  bindGAgent: (
    scopeId: string,
    actorTypeName: string,
    displayName?: string,
    serviceId?: string,
  ) =>
    request<any>(`/scopes/${enc(scopeId)}/binding`, {
      method: 'PUT',
      body: JSON.stringify({
        implementationKind: 'gagent',
        ...(displayName ? { displayName } : {}),
        ...(serviceId ? { serviceId } : {}),
        gagent: {
          actorTypeName,
          endpoints: [
            { endpointId: 'chat', displayName: 'Chat', kind: 'chat', requestTypeUrl: '', responseTypeUrl: '', description: '' },
          ],
        },
      }),
    }),

  /** POST /api/scopes/{scopeId}/services/{serviceId}/invoke/{endpointId}:stream — service invoke, SSE */
  streamInvoke: (
    scopeId: string,
    serviceId: string,
    prompt: string,
    onFrame?: (frame: any) => void,
    signal?: AbortSignal,
    endpointId: string = 'chat',
    headers?: Record<string, string>,
    actorId?: string,
    inputParts?: Array<{ type: string; text?: string; dataBase64?: string; mediaType?: string; uri?: string; name?: string }>,
  ) => {
    const body: any = { prompt };
    if (headers && Object.keys(headers).length > 0) body.headers = headers;
    if (actorId) body.actorId = actorId;
    if (inputParts && inputParts.length > 0) body.inputParts = inputParts;
    return streamSse(
      `/scopes/${enc(scopeId)}/services/${enc(serviceId)}/invoke/${enc(endpointId)}:stream`,
      body, onFrame ?? (() => {}), signal,
    );
  },

  /** GET /api/services?tenantId=... — list services in scope */
  listServices: (scopeId: string, take = 20) =>
    request<any[]>(`/services?tenantId=${enc(scopeId)}&appId=default&namespace=default&take=${take}`),

  /** POST /api/scopes/{scopeId}/services/{serviceId}/runs/{runId}:resume — resume a suspended workflow run (human_input) */
  resumeRun: (
    scopeId: string,
    serviceId: string,
    runId: string,
    data: { stepId: string; userInput?: string; approved?: boolean; actorId?: string },
  ) =>
    request<any>(`/scopes/${enc(scopeId)}/services/${enc(serviceId)}/runs/${enc(runId)}:resume`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** GET /api/actors/{actorId} — actor snapshot for run logs */
  getActorSnapshot: (actorId: string) =>
    request<any>(`/actors/${enc(actorId)}`),

};

/* ─── NyxID Chat APIs (runtime) ─── */
export const nyxidChat = {
  createConversation: (scopeId: string) =>
    request<{ actorId: string; createdAt: string }>(`/scopes/${enc(scopeId)}/nyxid-chat/conversations`, { method: 'POST' }),

  listConversations: (scopeId: string) =>
    request<Array<{ actorId: string; createdAt: string }>>(`/scopes/${enc(scopeId)}/nyxid-chat/conversations`),

  streamMessage: (
    scopeId: string,
    actorId: string,
    prompt: string,
    onFrame?: (frame: any) => void,
    signal?: AbortSignal,
  ) =>
    streamSse(
      `/scopes/${enc(scopeId)}/nyxid-chat/conversations/${enc(actorId)}:stream`,
      { prompt },
      onFrame ?? (() => {}),
      signal,
    ),

  deleteConversation: (scopeId: string, actorId: string) =>
    request<void>(`/scopes/${enc(scopeId)}/nyxid-chat/conversations/${enc(actorId)}`, { method: 'DELETE' }),

  /** Send tool approval decision and stream the continuation response. */
  approveToolCall: (
    scopeId: string,
    actorId: string,
    requestId: string,
    approved: boolean,
    onFrame?: (frame: any) => void,
    signal?: AbortSignal,
    sessionId?: string,
    reason?: string,
  ) =>
    streamSse(
      `/scopes/${enc(scopeId)}/nyxid-chat/conversations/${enc(actorId)}:approve`,
      { requestId, approved, reason: reason ?? '', sessionId: sessionId ?? '' },
      onFrame ?? (() => {}),
      signal,
    ),
};

/* ─── Streaming Proxy APIs (runtime) ─── */
export const streamingProxy = {
  createRoom: (scopeId: string, roomName?: string) =>
    request<{ roomId: string; roomName: string; createdAt: string }>(
      `/scopes/${enc(scopeId)}/streaming-proxy/rooms`,
      { method: 'POST', body: JSON.stringify(roomName ? { roomName } : {}) },
    ),

  streamChat: (
    scopeId: string,
    roomId: string,
    prompt: string,
    onFrame?: (frame: any) => void,
    signal?: AbortSignal,
    sessionId?: string,
    llmRoute?: string,
    llmModel?: string,
  ) => {
    const body: any = { prompt };
    if (sessionId) body.sessionId = sessionId;
    if (llmRoute !== undefined) body.llmRoute = llmRoute;
    if (llmModel !== undefined) body.llmModel = llmModel;
    return streamSse(
      `/scopes/${enc(scopeId)}/streaming-proxy/rooms/${enc(roomId)}:chat`,
      body,
      onFrame ?? (() => {}),
      signal,
    );
  },

  deleteRoom: (scopeId: string, roomId: string) =>
    request<void>(`/scopes/${enc(scopeId)}/streaming-proxy/rooms/${enc(roomId)}`, { method: 'DELETE' }),
};

/* ─── Chat History APIs (local, chrono-storage backed) ─── */
export const chatHistory = {
  getIndex: (scopeId: string) =>
    request<{ conversations: Array<{ id: string; title: string; serviceId: string; serviceKind: string; createdAt: string; updatedAt: string; messageCount: number; llmRoute?: string; llmModel?: string }> }>(
      `/scopes/${enc(scopeId)}/chat-history`,
    ),

  getConversation: (scopeId: string, convId: string) =>
    request<Array<{ id: string; role: string; content: string; authorId?: string; authorName?: string; timestamp: number; status: string; error?: string; thinking?: string }>>(
      `/scopes/${enc(scopeId)}/chat-history/conversations/${enc(convId)}`,
    ),

  saveConversation: (scopeId: string, convId: string, meta: any, messages: any[]) =>
    request<void>(`/scopes/${enc(scopeId)}/chat-history/conversations/${enc(convId)}`, {
      method: 'PUT',
      body: JSON.stringify({ meta, messages }),
    }),

  deleteConversation: (scopeId: string, convId: string) =>
    request<void>(`/scopes/${enc(scopeId)}/chat-history/conversations/${enc(convId)}`, {
      method: 'DELETE',
    }),
};

function enc(value: string) {
  return encodeURIComponent(value.trim());
}

/* ─── Ornn Skills Platform ─── */
export type OrnnSkillSummary = {
  guid?: string;
  name?: string;
  description?: string;
  isPrivate?: boolean;
  metadata?: { category?: string; tag?: string[] };
};

export type OrnnSearchResult = {
  total: number;
  totalPages: number;
  page: number;
  pageSize: number;
  items: OrnnSkillSummary[];
};

export const ornn = {
  /** Search skills on the Ornn platform directly (bearer token auth). */
  searchSkills: async (
    ornnBaseUrl: string,
    query = '',
    scope = 'mixed',
    page = 1,
    pageSize = 50,
  ): Promise<OrnnSearchResult> => {
    const url = `${ornnBaseUrl.replace(/\/+$/, '')}/api/web/skill-search?query=${encodeURIComponent(query)}&mode=keyword&scope=${encodeURIComponent(scope)}&page=${page}&pageSize=${pageSize}`;
    const res = await fetch(url, { headers: { ...getAuthHeaders() } });
    if (!res.ok) throw { status: res.status, message: `Ornn search failed: ${res.statusText}` };
    const json = await res.json();
    return json?.data || { total: 0, totalPages: 0, page: 1, pageSize, items: [] };
  },

  /** Check Ornn health / connectivity. */
  checkHealth: async (ornnBaseUrl: string): Promise<boolean> => {
    try {
      const url = `${ornnBaseUrl.replace(/\/+$/, '')}/api/web/skill-search?query=&scope=public&page=1&pageSize=1`;
      const res = await fetch(url, { headers: { ...getAuthHeaders() }, signal: AbortSignal.timeout(5000) });
      return res.ok;
    } catch {
      return false;
    }
  },
};

/* ─── Explorer (attachment upload only) ─── */
const encodeExplorerKey = (key: string) => key.split('/').map(segment => encodeURIComponent(segment)).join('/');

export const explorer = {
  uploadFile: async (key: string, file: File): Promise<{ key: string; size: number; contentType: string }> => {
    const form = new FormData();
    form.append('file', file);
    const resp = await fetch(`${BASE}/explorer/upload/${encodeExplorerKey(key)}`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: form,
      credentials: 'include',
    });
    if (!resp.ok) throw new Error(`Upload failed: ${resp.status}`);
    return resp.json();
  },
};
