import { Component, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { ManagementApi } from '../../core/services/management-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';

@Component({
  selector: 'ar-pairing',
  imports: [FormsModule, LucideAngularModule],
  template: `
    <main class="flex min-h-dvh flex-col bg-white">
      <div class="flex items-center gap-3 px-6 py-8 text-xl font-bold sm:px-11"><img src="mark.svg" alt="" width="36" height="36">AgentRouter</div>
      <section class="mx-auto my-auto w-[calc(100%-48px)] max-w-96 py-20" aria-labelledby="connect-title">
        <div class="mb-3 flex items-center gap-2 text-xs font-semibold text-emerald-700"><lucide-icon name="shield-check" [size]="16" />Local management</div>
        <h1 id="connect-title" class="mb-7 text-2xl font-bold">Connect this browser</h1>
        <form (ngSubmit)="connect()">
          <label for="pair-code" class="mb-2 block text-sm text-zinc-500">Pairing code</label>
          <input id="pair-code" class="h-14 text-center font-mono text-xl uppercase" name="code" [(ngModel)]="code" placeholder="XXXXXXXXXXXX" maxlength="12" minlength="12" required autocomplete="off" spellcheck="false" [disabled]="workspace.busy() || loading()">
          <button class="primary mt-4 w-full justify-between" type="submit" [disabled]="workspace.busy() || loading() || code.trim().length !== 12">{{ workspace.busy() ? 'Connecting...' : 'Connect' }}<lucide-icon name="arrow-right" [size]="18" /></button>
        </form>
        @if (workspace.error()) { <p class="mt-4 text-sm text-red-700" role="alert">{{ workspace.error() }}</p> }
        <div class="mt-7 flex items-center gap-2 border-t border-zinc-200 pt-5 text-xs text-zinc-500"><lucide-icon name="shield-check" [size]="15" />Private session · 15 minutes</div>
      </section>
      <footer class="flex justify-between gap-4 px-6 py-6 text-xs text-zinc-500 sm:px-11"><span>AgentRouter</span><span>Local-first model gateway</span></footer>
    </main>`,
})
export class PairingComponent {
  readonly workspace = inject(WorkspaceService);
  private readonly api = inject(ManagementApi);
  readonly loading = input(false);
  code = '';

  async connect(): Promise<void> {
    await this.workspace.run(async () => {
      await this.api.pair(this.code);
      this.code = '';
      await this.workspace.refresh();
    });
  }
}