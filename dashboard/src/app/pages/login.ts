import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api } from '../core/api';
import { Auth } from '../core/auth';
import { errorText } from '../core/toast';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="login-wrap">
      <div>
      <form class="card login" (ngSubmit)="submit()">
        <div class="brand big"><span class="logo">💬</span> ChatServe</div>
        <p class="muted">Dashboard for your restaurant's WhatsApp AI agent</p>
        <label class="field">
          <span>Username</span>
          <input name="username" [(ngModel)]="username" autofocus autocomplete="username" placeholder="admin" />
        </label>
        <label class="field">
          <span>Password</span>
          <input type="password" name="password" [(ngModel)]="password" autocomplete="current-password" />
        </label>
        @if (error()) { <div class="alert error">{{ error() }}</div> }
        <button class="btn primary block" [disabled]="busy() || !password">{{ busy() ? 'Logging in…' : 'Login' }}</button>
        <p class="muted small">Use the username and password ChatServe gave you.</p>
      </form>
      <p class="muted small site-links" style="text-align:center;margin-top:12px"><a href="/site">About ChatServe</a> · <a href="/site/privacy">Privacy Policy</a> · <a href="/site/terms">Terms</a></p>
      </div>
    </div>
  `,
})
export class Login {
  private api = inject(Api);
  private auth = inject(Auth);
  private router = inject(Router);
  username = '';
  password = '';
  busy = signal(false);
  error = signal<string | null>(null);

  async submit() {
    this.busy.set(true);
    this.error.set(null);
    try {
      const res = await this.api.login(this.username.trim() || 'admin', this.password);
      this.auth.set(res.accessToken);
      this.auth.me.set(await this.api.me());
      // The admin starts on the list of all clients; a client starts on their restaurant's home page.
      this.router.navigate([this.auth.isAdmin() ? '/clients' : '/']);
    } catch (e) {
      this.error.set(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
}
