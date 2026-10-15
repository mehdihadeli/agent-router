import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import type { Provider } from '../../core/models/management.models';
import { ManagementApi } from '../../core/services/management-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProviderEditorComponent } from './provider-editor.component';

@Component({
  selector: 'ar-providers',
  imports: [FormsModule, LucideAngularModule, ProviderEditorComponent],
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <input class="max-w-80" aria-label="Search providers" type="search" placeholder="Search providers or models" [ngModel]="search()" (ngModelChange)="search.set($event)">
      <button type="button" class="primary" (click)="edit()"><lucide-icon name="plus" [size]="17" />Add provider</button>
    </div>
    <div class="table-scroll"><table><thead><tr><th>Provider</th><th>Protocol</th><th>Models</th><th>Credential</th><th>Status</th><th><span class="sr-only">Actions</span></th></tr></thead>
      <tbody>@for (provider of providers(); track provider.Id) {
        <tr><td><strong>{{ provider.Id }}</strong><span class="cell-secondary">{{ provider.BaseUrl }}</span></td><td>{{ provider.Compatibility }}</td>
          <td><div class="flex flex-wrap gap-1">@for (model of provider.Models; track model) { <span class="model-tag">{{ model }}</span> }</div></td>
          <td><span class="inline-flex items-center gap-1 text-zinc-500"><lucide-icon name="shield-check" [size]="14" />{{ provider.HasApiKey ? 'Saved' : 'Missing' }}</span></td>
          <td><span class="state-pill" [class.offline]="!provider.Enabled">{{ provider.Enabled ? 'Enabled' : 'Disabled' }}</span></td>
          <td><div class="flex gap-1"><button class="icon-button" type="button" title="Edit provider" [attr.aria-label]="'Edit ' + provider.Id" (click)="edit(provider)"><lucide-icon name="pencil" [size]="16" /></button><button class="icon-button text-red-700" type="button" title="Delete provider" [attr.aria-label]="'Delete ' + provider.Id" [disabled]="workspace.busy()" (click)="remove(provider.Id)"><lucide-icon name="trash-2" [size]="16" /></button></div></td>
        </tr>
      } @empty { <tr><td colspan="6" class="empty-cell">{{ search() ? 'No matching providers.' : 'No providers configured.' }}</td></tr> }</tbody>
    </table></div>
    @if (editorOpen()) { <ar-provider-editor [provider]="selected()" (closed)="editorOpen.set(false)" /> }`,
})
export class ProvidersComponent {
  readonly workspace = inject(WorkspaceService);
  private readonly api = inject(ManagementApi);
  readonly search = signal('');
  readonly editorOpen = signal(false);
  readonly selected = signal<Provider | null>(null);
  readonly providers = computed(() => (this.workspace.config()?.Providers ?? []).filter(provider =>
    `${provider.Id} ${provider.Compatibility} ${provider.Models.join(' ')}`.toLowerCase().includes(this.search().toLowerCase())));

  edit(provider: Provider | null = null): void { this.workspace.error.set(''); this.selected.set(provider); this.editorOpen.set(true); }

  async remove(id: string): Promise<void> {
    if (!window.confirm(`Delete ${id}? Referenced providers cannot be removed.`)) return;
    await this.workspace.run(async () => {
      await this.api.request(`providers/${encodeURIComponent(id)}`, { method: 'DELETE' });
      await this.workspace.changed('Provider removed.');
    });
  }
}