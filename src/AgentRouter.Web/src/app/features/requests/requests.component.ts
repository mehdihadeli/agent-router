import { Component, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { WorkspaceService } from '../../core/services/workspace.service';

@Component({
  selector: 'ar-requests',
  imports: [DatePipe, DecimalPipe],
  template: `
    <div class="mb-4 flex flex-wrap justify-between gap-3 text-xs text-zinc-500"><span>Latest retained requests</span><span>Up to 100 records</span></div>
    <div class="table-scroll"><table><thead><tr><th>Time</th><th>Provider / model</th><th>Outcome</th><th>Latency</th><th>Input / output</th></tr></thead>
      <tbody>@for (request of workspace.requests(); track request.RequestId) {
        <tr><td>{{ request.Timestamp | date:'MMM d, HH:mm:ss' }}</td><td><strong>{{ request.Provider ?? 'Unknown' }}</strong><span class="cell-secondary font-mono">{{ request.Model ?? 'Unknown' }}</span></td><td><span class="state-pill" [class.offline]="request.Outcome !== 'ok'">{{ request.Outcome }}</span></td><td>{{ request.LatencyMs | number:'1.0-0' }} ms</td><td>{{ request.InputTokens ?? '-' }} / {{ request.OutputTokens ?? '-' }}</td></tr>
      } @empty { <tr><td colspan="5" class="empty-cell">No retained requests.</td></tr> }</tbody>
    </table></div>`,
})
export class RequestsComponent { readonly workspace = inject(WorkspaceService); }