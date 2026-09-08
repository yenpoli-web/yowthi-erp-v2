export interface AuthenticationSession {
  authenticated: boolean;
  accountId: string | null;
  displayName: string | null;
  capabilities: string[];
  developmentLoginAvailable: boolean;
}

export async function getAuthenticationSession(signal?: AbortSignal): Promise<AuthenticationSession> {
  const response = await fetch('/auth/session', {
    method: 'GET',
    credentials: 'include',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(`Authentication session request failed with HTTP ${response.status}.`);
  }

  return (await response.json()) as AuthenticationSession;
}

export async function developmentLogin(): Promise<AuthenticationSession> {
  const response = await fetch('/auth/development-login', {
    method: 'POST',
    credentials: 'include',
    headers: { Accept: 'application/json' },
  });

  if (!response.ok) {
    throw new Error(`Development login failed with HTTP ${response.status}.`);
  }

  return (await response.json()) as AuthenticationSession;
}

export async function logout(): Promise<void> {
  const csrfToken = document.cookie
    .split(';')
    .map((item) => item.trim())
    .find((item) => item.startsWith('XSRF-TOKEN='))
    ?.slice('XSRF-TOKEN='.length);

  const headers = new Headers();
  if (csrfToken) headers.set('X-CSRF-TOKEN', decodeURIComponent(csrfToken));

  const response = await fetch('/auth/logout', {
    method: 'POST',
    credentials: 'include',
    headers,
  });

  if (!response.ok && response.status !== 204) {
    throw new Error(`Logout failed with HTTP ${response.status}.`);
  }
}
