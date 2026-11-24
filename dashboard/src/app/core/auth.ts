import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

const KEY = 'chatserve.token';

function read(): string | null {
  try { return localStorage.getItem(KEY); } catch { return null; }
}

@Injectable({ providedIn: 'root' })
export class Auth {
  readonly token = signal<string | null>(read());

  set(token: string) {
    this.token.set(token);
    try { localStorage.setItem(KEY, token); } catch { /* private mode */ }
  }

  clear() {
    this.token.set(null);
    try { localStorage.removeItem(KEY); } catch { /* ignore */ }
  }
}

export const authGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  return auth.token() ? true : inject(Router).createUrlTree(['/login']);
};

/** Adds the token to every API call; on 401 (expired token) goes to the login page. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const token = auth.token();
  const authed = token && !req.url.endsWith('/api/auth/login')
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;
  return next(authed).pipe(
    catchError(err => {
      if (err?.status === 401 && token) {
        auth.clear();
        router.navigate(['/login']);
      }
      return throwError(() => err);
    }),
  );
};
