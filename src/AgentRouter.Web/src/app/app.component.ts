import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import type { View } from './core/models/management.models';
import { ManagementApi } from './core/services/management-api.service';
import { WorkspaceService } from './core/services/workspace.service';
import { PairingComponent } from './features/pairing/pairing.component';
import { OverviewComponent } from './features/overview/overview.component';
import { ProvidersComponent } from './features/providers/providers.component';
import { RoutingComponent } from './features/routing/routing.component';
import { ProfilesComponent } from './features/profiles/profiles.component';
import { RequestsComponent } from './features/requests/requests.component';

@Component({
  selector: 'ar-app',
  imports: [LucideAngularModule, PairingComponent, OverviewComponent, ProvidersComponent, RoutingComponent, ProfilesComponent, RequestsComponent],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnInit, OnDestroy {
  readonly api = inject(ManagementApi);
  readonly workspace = inject(WorkspaceService);
  readonly view = signal<View>('Overview');
  readonly loading = signal(true);
  readonly navigation: { name: View; icon: string }[] = [
    { name: 'Overview', icon: 'layout-dashboard' }, { name: 'Providers', icon: 'server' },
    { name: 'Routing', icon: 'git-branch' }, { name: 'Profiles', icon: 'terminal' },
    { name: 'Requests', icon: 'activity' },
  ];
  private timer?: ReturnType<typeof setInterval>;

  async ngOnInit(): Promise<void> {
    await this.api.restore();
    if (this.api.authenticated()) await this.workspace.run(() => this.workspace.refresh());
    this.loading.set(false);
    this.timer = setInterval(() => {
      if (this.api.authenticated() && !this.workspace.busy() && !document.hidden) void this.workspace.poll();
    }, 10000);
  }

  ngOnDestroy(): void { clearInterval(this.timer); }

  async disconnect(): Promise<void> {
    await this.workspace.run(async () => { await this.api.logout(); this.workspace.reset(); this.view.set('Overview'); });
  }
}