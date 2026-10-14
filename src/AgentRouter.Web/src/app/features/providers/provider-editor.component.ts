import { Component, inject, input, OnDestroy, OnInit, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import type { Provider, ProviderRequest } from '../../core/models/management.models';
import { ManagementApi } from '../../core/services/management-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ModalComponent } from '../../shared/ui/modal.component';

@Component({
  selector: 'ar-provider-editor',
  imports: [FormsModule, LucideAngularModule, ModalComponent],
  template: `
    <ar-modal [title]="provider() ? 'Edit provider' : 'Add provider'" [busy]="workspace.busy()" [error]="workspace.error()" (closed)="closed.emit()">
      <form (ngSubmit)="save()" #form="ngForm">
        <label for="provider-id">Provider ID</label><input id="provider-id" name="id" [(ngModel)]="draft.id" required pattern="[A-Za-z0-9._-]+" maxlength="100" [disabled]="!!provider()" autocomplete="off">
        <label for="provider-protocol">Protocol</label><select id="provider-protocol" name="protocol" [(ngModel)]="draft.Compatibility"><option value="OpenAi">OpenAI Chat Completions</option><option value="Anthropic">Anthropic Messages</option></select>
        <label for="provider-url">API base URL</label><input id="provider-url" type="url" name="url" [(ngModel)]="draft.BaseUrl" required placeholder="https://api.openai.com/v1" autocomplete="off">
        <label for="provider-models">Model IDs</label><textarea id="provider-models" name="models" [(ngModel)]="draft.models" required rows="3" placeholder="gpt-4.1" spellcheck="false"></textarea>
        <label for="provider-key">{{ provider() ? 'Replace API key (optional)' : 'API key' }}</label><input id="provider-key" type="password" name="key" [(ngModel)]="draft.ApiKey" [required]="!provider()" autocomplete="new-password">
        @if (provider()) { <label class="checkbox-label"><input type="checkbox" name="enabled" [(ngModel)]="draft.Enabled">Enabled</label> }
        <div class="modal-actions"><button class="secondary" type="button" (click)="closed.emit()" [disabled]="workspace.busy()">Cancel</button><button class="primary" type="submit" [disabled]="workspace.busy() || form.invalid"><lucide-icon name="check" [size]="16" />{{ workspace.busy() ? 'Saving...' : 'Save provider' }}</button></div>
      </form>
    </ar-modal>`,
})
export class ProviderEditorComponent implements OnInit, OnDestroy {
  readonly workspace = inject(WorkspaceService);
  private readonly api = inject(ManagementApi);
  readonly provider = input<Provider | null>(null);
  readonly closed = output<void>();
  draft = { id: '', BaseUrl: '', Compatibility: 'OpenAi' as Provider['Compatibility'], models: '', ApiKey: '', Enabled: true };

  ngOnInit(): void {
    const provider = this.provider();
    if (provider) this.draft = { id: provider.Id, BaseUrl: provider.BaseUrl ?? '', Compatibility: provider.Compatibility, models: provider.Models.join('\n'), ApiKey: '', Enabled: provider.Enabled };
  }

  ngOnDestroy(): void { this.draft.ApiKey = ''; }

  async save(): Promise<void> {
    const request: ProviderRequest = {
      BaseUrl: this.draft.BaseUrl.trim(), Compatibility: this.draft.Compatibility,
      Models: this.draft.models.split(/[\n,]/).map(model => model.trim()).filter(Boolean),
      Enabled: this.draft.Enabled, ...(this.draft.ApiKey ? { ApiKey: this.draft.ApiKey } : {}),
    };
    const saved = await this.workspace.run(async () => {
      await this.api.request(`providers/${encodeURIComponent(this.draft.id)}`, { method: this.provider() ? 'PUT' : 'POST', body: JSON.stringify(request) });
      this.draft.ApiKey = '';
      await this.workspace.changed('Provider saved.');
    });
    if (saved) this.closed.emit();
  }
}