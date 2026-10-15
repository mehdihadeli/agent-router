import { Component, inject, input, OnInit, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import type { Profile, ProfileRequest } from '../../core/models/management.models';
import { ManagementApi } from '../../core/services/management-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ModalComponent } from '../../shared/ui/modal.component';

@Component({
  selector: 'ar-profile-editor',
  imports: [FormsModule, LucideAngularModule, ModalComponent],
  template: `
    <ar-modal [title]="profile() ? 'Edit profile' : 'Add profile'" [busy]="workspace.busy()" [error]="workspace.error()" (closed)="closed.emit()">
      <form (ngSubmit)="save()" #form="ngForm">
        <label for="profile-name">Profile name</label><input id="profile-name" name="name" [(ngModel)]="draft.name" required [disabled]="!!profile()" autocomplete="off">
        <label for="profile-kind">Agent</label><select id="profile-kind" name="kind" [(ngModel)]="draft.Kind" (ngModelChange)="kindChanged()"><option value="claude">Claude Code</option><option value="codex">Codex</option><option value="opencode">OpenCode</option><option value="claude-design">Claude Design</option></select>
        <label for="profile-model">Model</label><select id="profile-model" name="model" [(ngModel)]="draft.Model"><option value="">Default route</option>@for (model of workspace.models(); track model) { <option [value]="model">{{ model }}</option> }</select>
        <label for="profile-executable">Native executable</label><input id="profile-executable" name="executable" [(ngModel)]="draft.Executable" required placeholder="claude" autocomplete="off">
        <label for="profile-entry-mode">Entry mode</label><select id="profile-entry-mode" name="entryMode" [(ngModel)]="draft.EntryMode" [disabled]="draft.Kind === 'claude-design'"><option value="cli">CLI</option><option value="app">App</option><option value="cli-and-app">CLI and app</option></select>
        <label for="profile-scope">Scope</label><select id="profile-scope" name="scope" [(ngModel)]="draft.Scope" [disabled]="draft.Kind === 'claude-design'"><option value="launch">Opened from Agent Router</option><option value="system">System default metadata</option></select>
        <label for="profile-telemetry">Native telemetry endpoint</label><input id="profile-telemetry" name="telemetryEndpoint" type="url" [(ngModel)]="draft.TelemetryEndpoint" placeholder="Optional HTTPS or loopback HTTP URL" autocomplete="url">
        <label for="profile-working-directory">Working directory</label><input id="profile-working-directory" name="workingDirectory" [(ngModel)]="draft.WorkingDirectory" placeholder="Optional absolute path" autocomplete="off">
        <fieldset><legend>Claude tier models</legend><label for="profile-opus">Opus</label><select id="profile-opus" name="opus" [(ngModel)]="draft.TierModels.opus"><option value="">Inherit route</option>@for (model of workspace.models(); track model) { <option [value]="model">{{ model }}</option> }</select><label for="profile-sonnet">Sonnet</label><select id="profile-sonnet" name="sonnet" [(ngModel)]="draft.TierModels.sonnet"><option value="">Inherit route</option>@for (model of workspace.models(); track model) { <option [value]="model">{{ model }}</option> }</select><label for="profile-haiku">Haiku</label><select id="profile-haiku" name="haiku" [(ngModel)]="draft.TierModels.haiku"><option value="">Inherit route</option>@for (model of workspace.models(); track model) { <option [value]="model">{{ model }}</option> }</select></fieldset>
        @if (profile()) { <label class="checkbox-label"><input type="checkbox" name="enabled" [(ngModel)]="draft.Enabled">Enabled</label> }
        <div class="modal-actions"><button class="secondary" type="button" (click)="closed.emit()" [disabled]="workspace.busy()">Cancel</button><button class="primary" type="submit" [disabled]="workspace.busy() || form.invalid"><lucide-icon name="check" [size]="16" />{{ workspace.busy() ? 'Saving...' : 'Save profile' }}</button></div>
      </form>
    </ar-modal>`,
})
export class ProfileEditorComponent implements OnInit {
  readonly workspace = inject(WorkspaceService);
  private readonly api = inject(ManagementApi);
  readonly profile = input<Profile | null>(null);
  readonly closed = output<void>();
  draft = {
    name: '', Model: '', Executable: 'claude', Enabled: true,
    Kind: 'claude', EntryMode: 'cli', Scope: 'launch',
    TelemetryEndpoint: '',
    WorkingDirectory: '',
    TierModels: { opus: '', sonnet: '', haiku: '' },
  };

  ngOnInit(): void {
    const profile = this.profile();
    if (profile) {
      this.draft = {
        name: profile.Name, Model: profile.Model ?? '', Executable: profile.Executable, Enabled: profile.Enabled,
        Kind: profile.Kind, EntryMode: profile.EntryMode, Scope: profile.Scope,
        TelemetryEndpoint: profile.TelemetryEndpoint ?? '',
        WorkingDirectory: profile.WorkingDirectory ?? '',
        TierModels: {
          opus: profile.TierModels['opus'] ?? '',
          sonnet: profile.TierModels['sonnet'] ?? '',
          haiku: profile.TierModels['haiku'] ?? '',
        },
      };
    }
  }

  kindChanged(): void {
    if (this.draft.Kind === 'claude-design') this.draft.EntryMode = 'app';
  }

  async save(): Promise<void> {
    const TierModels = Object.fromEntries(Object.entries(this.draft.TierModels).filter(([, model]) => model));
    const request: ProfileRequest = {
      Model: this.draft.Model || null, Executable: this.draft.Executable, Enabled: this.draft.Enabled,
      Kind: this.draft.Kind, EntryMode: this.draft.EntryMode, Scope: this.draft.Scope, TierModels,
      TelemetryEndpoint: this.draft.TelemetryEndpoint || null,
      WorkingDirectory: this.draft.WorkingDirectory || null,
    };
    const saved = await this.workspace.run(async () => {
      await this.api.request(`profiles/${encodeURIComponent(this.draft.name)}`, { method: this.profile() ? 'PUT' : 'POST', body: JSON.stringify(request) });
      await this.workspace.changed('Profile saved.');
    });
    if (saved) this.closed.emit();
  }
}