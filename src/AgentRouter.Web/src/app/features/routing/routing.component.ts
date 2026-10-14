import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { ROUTE_TIERS, RouteTier } from '../../core/models/management.models';
import { ManagementApi } from '../../core/services/management-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';

@Component({
  selector: 'ar-routing',
  imports: [FormsModule, LucideAngularModule],
  template: `
    <section class="route-editor">
      @for (tier of tiers; track tier) {
        <div class="route-editor-row">
          <div><label [for]="'route-' + tier">{{ tier }}</label><span class="cell-secondary">{{ tier === 'Default' ? 'Fallback for unrecognized client models' : 'Claude ' + tier + ' tier' }}</span></div>
          <select [id]="'route-' + tier" [ngModel]="routes()[tier]" (ngModelChange)="setRoute(tier, $event)">
            <option value="">Not assigned</option>
            @for (model of workspace.models(); track model) { <option [value]="model">{{ model }}</option> }
          </select>
          <button class="secondary" type="button" [disabled]="workspace.busy() || !routes()[tier]" (click)="saveRoute(tier)"><lucide-icon name="check" [size]="16" />Save</button>
        </div>
      }
    </section>`,
})
export class RoutingComponent {
  readonly workspace = inject(WorkspaceService);
  private readonly api = inject(ManagementApi);
  readonly tiers = ROUTE_TIERS;
  readonly routes = signal<Record<string, string>>({});

  constructor() {
    effect(() => this.routes.set(Object.fromEntries((this.workspace.config()?.Routes ?? []).map(route => [
      route.Tier, route.ProviderId && route.Model ? `${route.ProviderId}/${route.Model}` : '',
    ]))));
  }

  setRoute(tier: RouteTier, value: string): void { this.routes.update(routes => ({ ...routes, [tier]: value })); }

  async saveRoute(tier: RouteTier): Promise<void> {
    await this.workspace.run(async () => {
      await this.api.request(`routes/${tier}`, { method: 'PUT', body: JSON.stringify({ Model: this.routes()[tier] }) });
      await this.workspace.changed(`${tier} route saved.`);
    });
  }
}