import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Toasts } from './core/toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `
    <router-outlet />
    <div class="toasts">
      @for (t of toasts.items(); track t.id) {
        <div class="toast" [class.error]="t.kind === 'error'" (click)="toasts.dismiss(t.id)">{{ t.text }}</div>
      }
    </div>
  `,
})
export class App {
  protected toasts = inject(Toasts);
}
