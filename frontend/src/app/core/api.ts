import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router, CanActivateFn } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Catalog, User } from './models';

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  user = signal<User | null>(null);
  catalog = signal<Catalog | null>(null);
  async get<T>(path: string): Promise<T> {
    try {
      return await firstValueFrom(this.http.get<T>('/api' + path));
    } catch (error) {
      throw this.error(error);
    }
  }
  async send<T = void>(method: string, path: string, body?: unknown): Promise<T> {
    try {
      const csrf = await this.get<{ token: string }>('/auth/csrf');
      return await firstValueFrom(
        this.http.request<T>(method, '/api' + path, {
          body,
          headers: { 'X-XSRF-TOKEN': csrf.token },
        }),
      );
    } catch (error) {
      throw this.error(error);
    }
  }
  async session() {
    try {
      this.user.set(await this.get<User>('/auth/me'));
    } catch {
      this.user.set(null);
    }
  }
  async loadCatalog() {
    this.catalog.set(await this.get<Catalog>('/catalog'));
  }
  async logout() {
    await this.send('POST', '/auth/logout');
    this.user.set(null);
  }
  private error(error: unknown): Error {
    if (error instanceof HttpErrorResponse)
      return new Error(
        error.error?.detail ??
          (error.status === 401
            ? 'Please sign in to continue.'
            : error.status === 403
              ? 'Administrator access is required.'
              : error.status === 429
                ? 'Too many requests. Please try again shortly.'
                : 'The request failed. Check your connection and try again.'),
      );
    return error instanceof Error ? error : new Error('An unexpected error occurred.');
  }
}
export const memberGuard: CanActivateFn = async () => {
  const api = inject(Api),
    router = inject(Router);
  await api.session();
  return api.user() ? true : router.createUrlTree(['/login']);
};
export const adminGuard: CanActivateFn = async () => {
  const api = inject(Api),
    router = inject(Router);
  await api.session();
  return api.user()?.isAdmin ? true : router.createUrlTree(['/news']);
};
export class PageState {
  busy = signal(false);
  error = signal('');
  message = signal('');
  async perform(action: () => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Something went wrong.');
    } finally {
      this.busy.set(false);
    }
  }
}
