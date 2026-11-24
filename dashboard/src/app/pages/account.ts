import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api } from '../core/api';
import { Auth } from '../core/auth';
import { errorText, Toasts } from '../core/toast';

/** Restaurant owners: change their own password. */
@Component({
  selector: 'app-account',
  imports: [FormsModule],
  template: `
    <div class="page-head"><h1>My account</h1></div>
    <form class="card form-col narrow" (ngSubmit)="save()">
      <p class="muted">Signed in as <b>{{ auth.me()?.username }}</b>.</p>
      <label class="field"><span>Current password</span>
        <input name="cur" type="password" [(ngModel)]="current" autocomplete="current-password" /></label>
      <label class="field"><span>New password (min 8)</span>
        <input name="new" type="password" [(ngModel)]="next" autocomplete="new-password" /></label>
      <label class="field"><span>Repeat new password</span>
        <input name="rep" type="password" [(ngModel)]="repeat" autocomplete="new-password" /></label>
      <div><button class="btn primary" [disabled]="busy() || !current || !next">Change password</button></div>
      <p class="muted small">After changing it you'll be asked to log in again on every device.</p>
    </form>
  `,
})
export class AccountPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private router = inject(Router);
  protected auth = inject(Auth);
  busy = signal(false);
  current = '';
  next = '';
  repeat = '';

  async save() {
    if (this.next !== this.repeat) { this.toasts.error("The new passwords don't match."); return; }
    this.busy.set(true);
    try {
      await this.api.changePassword(this.current, this.next);
      this.toasts.ok('Password changed. Please log in with the new password.');
      this.auth.clear();
      this.router.navigate(['/login']);
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
}
