import { Component, inject, signal } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import type { Profile } from '../../core/models/management.models';
import { ManagementApi } from '../../core/services/management-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProfileEditorComponent } from './profile-editor.component';

@Component({
  selector: 'ar-profiles',
  imports: [LucideAngularModule, ProfileEditorComponent],
  template: `
    <div class="mb-6 flex justify-end"><button type="button" class="primary" (click)="edit()"><lucide-icon name="plus" [size]="17" />Add profile</button></div>
    <div class="table-scroll"><table><thead><tr><th>Profile</th><th>Agent</th><th>Model</th><th>Surface</th><th>Scope</th><th>Status</th><th><span class="sr-only">Actions</span></th></tr></thead>
      <tbody>@for (profile of workspace.config()?.Profiles ?? []; track profile.Name) {
        <tr><td><strong>{{ profile.Name }}</strong><div class="text-xs text-slate-500 font-mono">{{ profile.Executable }}</div></td><td>{{ profile.Kind }}</td><td class="font-mono">{{ profile.Model ?? 'Default route' }}</td><td>{{ profile.EntryMode }}</td><td>{{ profile.Scope }}</td><td><span class="state-pill" [class.offline]="!profile.Enabled">{{ profile.Enabled ? 'Enabled' : 'Disabled' }}</span></td>
          <td><div class="flex gap-1"><button class="icon-button" type="button" title="Edit profile" [attr.aria-label]="'Edit ' + profile.Name" (click)="edit(profile)"><lucide-icon name="pencil" [size]="16" /></button><button class="icon-button text-red-700" type="button" title="Delete profile" [attr.aria-label]="'Delete ' + profile.Name" [disabled]="workspace.busy()" (click)="remove(profile.Name)"><lucide-icon name="trash-2" [size]="16" /></button></div></td>
        </tr>
      } @empty { <tr><td colspan="7" class="empty-cell">No profiles configured.</td></tr> }</tbody>
    </table></div>
    @if (editorOpen()) { <ar-profile-editor [profile]="selected()" (closed)="editorOpen.set(false)" /> }`,
})
export class ProfilesComponent {
  readonly workspace = inject(WorkspaceService);
  private readonly api = inject(ManagementApi);
  readonly selected = signal<Profile | null>(null);
  readonly editorOpen = signal(false);

  edit(profile: Profile | null = null): void { this.workspace.error.set(''); this.selected.set(profile); this.editorOpen.set(true); }

  async remove(name: string): Promise<void> {
    if (!window.confirm(`Delete profile ${name}?`)) return;
    await this.workspace.run(async () => {
      await this.api.request(`profiles/${encodeURIComponent(name)}`, { method: 'DELETE' });
      await this.workspace.changed('Profile removed.');
    });
  }
}