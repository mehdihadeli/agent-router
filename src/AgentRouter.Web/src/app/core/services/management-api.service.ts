import { Injectable, signal } from '@angular/core';
import type { BrowserSession } from '../models/management.models';

@Injectable({ providedIn: 'root' })
export class ManagementApi {
  readonly authenticated = signal(false);
  private csrf = '';

  async restore(): Promise<void> {
    try { this.accept(await this.request<BrowserSession>('browser/session')); }
    catch { this.authenticated.set(false); }
  }

  async pair(code: string): Promise<void> {
    this.accept(await this.request<BrowserSession>('browser/session', {
      method: 'POST', body: JSON.stringify({ Code: code }),
    }));
  }

  async logout(): Promise<void> {
    await this.request('browser/session', { method: 'DELETE' });
    this.csrf = '';
    this.authenticated.set(false);
  }

  async request<T = unknown>(path: string, options: RequestInit = {}): Promise<T> {
    const response = await fetch(`/management/v1/${path}`, {
      ...options, credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-AR-CSRF': this.csrf, ...options.headers },
    });
    if (response.status === 401) {
      this.csrf = '';
      this.authenticated.set(false);
      throw new Error(path === 'browser/session' && options.method === 'POST'
        ? 'Code is invalid or expired. Generate a new code with ar ui.'
        : 'Session expired. Run ar ui to connect again.');
    }
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      throw new Error(problem.title ?? problem.Error ?? (response.status === 429
        ? 'Too many pairing attempts. Try again in one minute.'
        : `Request failed (${response.status}). No successful change was confirmed.`));
    }
    return response.status === 204 ? undefined as T : await response.json() as T;
  }

  private accept(session: BrowserSession): void {
    this.csrf = session.CsrfToken;
    this.authenticated.set(true);
  }
}