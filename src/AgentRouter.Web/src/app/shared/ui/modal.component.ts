import { Component, effect, ElementRef, input, OnDestroy, output, viewChild } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';

@Component({
  selector: 'ar-modal',
  imports: [LucideAngularModule],
  template: `
    <dialog #dialog class="modal" [attr.aria-label]="title()" (cancel)="$event.preventDefault(); close()">
      <header class="modal-header"><h2>{{ title() }}</h2>
        <button class="icon-button" type="button" title="Close" aria-label="Close dialog" [disabled]="busy()" (click)="close()"><lucide-icon name="x" [size]="19" /></button>
      </header>
      @if (error()) { <div class="banner error" role="alert">{{ error() }}</div> }
      <ng-content />
    </dialog>`,
})
export class ModalComponent implements OnDestroy {
  readonly title = input.required<string>();
  readonly busy = input(false);
  readonly error = input('');
  readonly closed = output<void>();
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const dialog = this.dialog()?.nativeElement;
      if (dialog && !dialog.open) dialog.showModal();
    });
  }

  close(): void {
    if (!this.busy()) { this.dialog()?.nativeElement.close(); this.closed.emit(); }
  }

  ngOnDestroy(): void { this.dialog()?.nativeElement.close(); }
}