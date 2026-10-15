import { computed, inject, Injectable, signal } from '@angular/core';
import type { Configuration, Gateway, GatewayAction, ObservabilitySummary, RequestSummary } from '../models/management.models';
import { ManagementApi } from './management-api.service';

@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  private readonly api = inject(ManagementApi);
  readonly config = signal<Configuration | null>(null);
  readonly gateway = signal<Gateway | null>(null);
  readonly requests = signal<RequestSummary[]>([]);
  readonly observability = signal<ObservabilitySummary | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly savedChanges = signal(false);
  readonly models = computed(() => (this.config()?.Providers ?? [])
    .filter(provider => provider.Enabled)
    .flatMap(provider => provider.Models.map(model => `${provider.Id}/${model}`)));

  async refresh(): Promise<void> {
    const [config, gateway, requests, observability] = await Promise.all([
      this.api.request<Configuration>('config'), this.api.request<Gateway>('gateway'),
      this.api.request<RequestSummary[]>('requests?limit=100'),
      this.api.request<ObservabilitySummary>('observability?hours=24'),
    ]);
    this.config.set(config);
    this.gateway.set(gateway);
    this.requests.set(requests);
    this.observability.set(observability);
  }

  async control(action: GatewayAction): Promise<void> {
    await this.run(async () => {
      this.gateway.set(await this.api.request<Gateway>(`gateway/${action}`, { method: 'POST' }));
      if (action !== 'stop') this.savedChanges.set(false);
      this.notice.set(action === 'stop' ? 'Inference paused. Management remains available.' : 'Inference is running.');
    });
  }

  async changed(message: string): Promise<void> {
    this.savedChanges.set(true);
    await this.refresh();
    this.notice.set(message);
  }

  async run(action: () => Promise<void>): Promise<boolean> {
    if (this.busy()) return false;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try { await action(); return true; }
    catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Connection failed. Try again.');
      return false;
    } finally { this.busy.set(false); }
  }

  async poll(): Promise<void> {
    try { this.gateway.set(await this.api.request<Gateway>('gateway')); }
    catch (error) { this.error.set(error instanceof Error ? error.message : 'Management is unavailable.'); }
  }

  reset(): void {
    this.config.set(null);
    this.gateway.set(null);
    this.requests.set([]);
    this.observability.set(null);
    this.savedChanges.set(false);
    this.error.set('');
    this.notice.set('');
  }
}