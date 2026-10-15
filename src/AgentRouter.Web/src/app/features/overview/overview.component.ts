import { Component, inject, output } from '@angular/core';
import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceService } from '../../core/services/workspace.service';

@Component({
  selector: 'ar-overview',
  imports: [DatePipe, DecimalPipe, PercentPipe, LucideAngularModule],
  template: `
    @if (workspace.config(); as config) {
      <section class="flex flex-wrap items-center justify-between gap-6 border-y border-emerald-100 bg-emerald-50/60 p-6" aria-labelledby="gateway-title">
        <div><div class="mb-3 text-[10px] font-bold text-emerald-700">INFERENCE GATEWAY</div>
          <div class="flex flex-wrap items-center gap-3"><h2 id="gateway-title" class="text-xl font-bold">{{ workspace.gateway()?.State === 'Running' ? 'Ready for requests' : workspace.gateway()?.State === 'Failed' ? 'Needs attention' : 'Gateway is stopped' }}</h2><span class="state-pill" [class.offline]="workspace.gateway()?.State !== 'Running'">{{ workspace.gateway()?.State ?? 'Stopped' }}</span></div>
          <div class="mt-3 break-all font-mono text-xs text-zinc-500">{{ workspace.gateway()?.Url ?? '127.0.0.1:' + config.ProxyPort }}</div>
          @if (workspace.gateway()?.Error) { <p class="mt-3 text-sm text-red-700">{{ workspace.gateway()?.Error }}</p> }
        </div>
        <div class="flex flex-wrap gap-2">@if (workspace.gateway()?.State === 'Running') {
          <button class="secondary" type="button" (click)="workspace.control('stop')" [disabled]="workspace.busy()"><lucide-icon name="pause" [size]="16" />Pause</button>
          <button class="primary" type="button" (click)="workspace.control('restart')" [disabled]="workspace.busy()"><lucide-icon name="refresh-cw" [size]="16" />Restart</button>
        } @else { <button class="primary" type="button" (click)="workspace.control('start')" [disabled]="workspace.busy() || !workspace.models().length"><lucide-icon name="play" [size]="16" />Start gateway</button> }</div>
      </section>
      <section class="my-7 grid grid-cols-2 gap-6 border-b border-zinc-200 pb-7 lg:grid-cols-4" aria-label="Workspace summary">
        @for (stat of [{name:'Providers',value:config.Providers.length,icon:'server'}, {name:'Enabled models',value:workspace.models().length,icon:'git-branch'}, {name:'Agent profiles',value:config.Profiles.length,icon:'terminal'}, {name:'Retained requests',value:workspace.requests().length,icon:'activity'}]; track stat.name) {
          <div><span class="text-xs text-zinc-500">{{ stat.name }}</span><strong class="mt-2 flex items-center justify-between text-3xl font-semibold">{{ stat.value }}<lucide-icon class="text-zinc-400" [name]="stat.icon" [size]="20" /></strong></div>
        }
      </section>
      @if (workspace.observability(); as metrics) {
        <section class="mb-8 border-y border-zinc-200 py-6" aria-labelledby="observability-title">
          <div class="mb-5 flex flex-wrap items-center justify-between gap-3"><div><div class="mb-1 text-[10px] font-bold uppercase tracking-[0.18em] text-emerald-700">OpenTelemetry-backed gateway view</div><h2 id="observability-title" class="text-sm font-bold">Observability · last 24 hours</h2></div><span class="text-xs text-zinc-500">Sanitized retained request summaries</span></div>
          <div class="grid grid-cols-2 gap-5 lg:grid-cols-6">
            <div><span class="text-xs text-zinc-500">Requests</span><strong class="mt-1 block text-2xl font-semibold">{{ metrics.Requests | number }}</strong></div>
            <div><span class="text-xs text-zinc-500">Success rate</span><strong class="mt-1 block text-2xl font-semibold">{{ metrics.SuccessRate | percent:'1.0-1' }}</strong></div>
            <div><span class="text-xs text-zinc-500">Errors</span><strong class="mt-1 block text-2xl font-semibold">{{ metrics.FailedRequests | number }}</strong></div>
            <div><span class="text-xs text-zinc-500">P95 latency</span><strong class="mt-1 block text-2xl font-semibold">{{ metrics.P95LatencyMs | number:'1.0-0' }}<small class="ml-1 text-xs font-normal text-zinc-500">ms</small></strong></div>
            <div><span class="text-xs text-zinc-500">Input / output</span><strong class="mt-1 block text-2xl font-semibold">{{ metrics.InputTokens | number }}<small class="mx-1 text-xs font-normal text-zinc-500">/</small>{{ metrics.OutputTokens | number }}</strong></div>
            <div><span class="text-xs text-zinc-500">Top route</span><strong class="mt-1 block truncate text-sm font-semibold" [title]="(metrics.TopProvider ?? 'Unknown') + ' / ' + (metrics.TopModel ?? 'Unknown')">{{ metrics.TopProvider ?? 'Unknown' }}<span class="block truncate font-mono text-xs font-normal text-zinc-500">{{ metrics.TopModel ?? 'Unknown' }}</span></strong></div>
          </div>
          <div class="mt-5 flex flex-wrap gap-x-6 gap-y-2 border-t border-zinc-100 pt-3 text-xs text-zinc-500"><span>Average latency: {{ metrics.AverageLatencyMs | number:'1.0-0' }} ms</span><span>Last request: {{ metrics.LastRequestAt ? (metrics.LastRequestAt | date:'MMM d, HH:mm:ss') : 'None' }}</span><span>Cost: {{ metrics.CostUsd === null ? 'Unknown' : ('$' + metrics.CostUsd) }}</span></div>
        </section>
      }
      <section class="mb-8"><div class="mb-4 flex items-center justify-between gap-4"><h2 class="text-sm font-bold">Connected providers</h2><button class="text-button" type="button" (click)="manageProviders.emit()">Manage providers<lucide-icon name="arrow-right" [size]="16" /></button></div>
        <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">@for (provider of config.Providers; track provider.Id) {
          <article class="min-w-0 rounded-md border border-zinc-200 bg-white p-5"><div class="mb-4 flex items-center justify-between gap-2"><span class="grid size-9 place-items-center rounded-md bg-emerald-50 font-bold text-emerald-800">{{ provider.Id.slice(0,1).toUpperCase() }}</span><span class="state-pill" [class.offline]="!provider.Enabled">{{ provider.Enabled ? 'Enabled' : 'Disabled' }}</span></div><h3 class="text-sm font-bold">{{ provider.Id }}</h3><p class="mt-1 truncate text-xs text-zinc-500">{{ provider.BaseUrl }}</p><div class="mt-4 flex items-center justify-between border-t border-zinc-100 pt-3 text-xs text-zinc-500"><span>{{ provider.Models.length }} models</span><button class="icon-button" type="button" title="Manage providers" aria-label="Manage providers" (click)="manageProviders.emit()"><lucide-icon name="arrow-right" [size]="17" /></button></div></article>
        } @empty { <div class="empty-state md:col-span-2 xl:col-span-3"><lucide-icon name="server" [size]="28" /><h3>No providers yet</h3><button class="secondary" type="button" (click)="manageProviders.emit()">Manage providers<lucide-icon name="arrow-right" [size]="16" /></button></div> }</div>
      </section>
      <section><div class="mb-4 flex items-center justify-between gap-4"><h2 class="text-sm font-bold">Model routes</h2><button class="text-button" type="button" (click)="manageRouting.emit()">Edit routes<lucide-icon name="arrow-right" [size]="16" /></button></div><div class="border-t border-zinc-200">@for (route of config.Routes; track route.Tier) { <div class="grid grid-cols-[70px_20px_minmax(0,1fr)] items-center gap-3 border-b border-zinc-200 py-4 text-xs"><span class="font-semibold">{{ route.Tier }}</span><lucide-icon class="text-zinc-400" name="arrow-right" [size]="15" /><code class="wrap-anywhere text-zinc-500">{{ route.ProviderId && route.Model ? route.ProviderId + '/' + route.Model : 'Not assigned' }}</code></div> }</div></section>
    }`,
})
export class OverviewComponent {
  readonly workspace = inject(WorkspaceService);
  readonly manageProviders = output<void>();
  readonly manageRouting = output<void>();
}