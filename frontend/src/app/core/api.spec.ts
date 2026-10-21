import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { Api } from './api';

describe('API session and CSRF protection', () => {
  let api: Api;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(Api);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });
  it('fetches a fresh CSRF token before a state-changing request', async () => {
    const pending = api.send('PUT', '/subscriptions', { categories: ['health'] });
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh-token' });
    const request = await vi.waitFor(() => http.expectOne('/api/subscriptions'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('fresh-token');
    expect(request.request.body.categories).toEqual(['health']);
    request.flush(null);
    await pending;
  });
  it('clears the session when authentication expires', async () => {
    api.user.set({
      id: 'u1',
      email: 'reader@example.com',
      displayName: 'Reader',
      emailConfirmed: true,
      isAdmin: false,
    });
    const pending = api.session();
    http.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await pending;
    expect(api.user()).toBeNull();
  });
  it('presents server validation details', async () => {
    const pending = api.get('/news?period=bad');
    const assertion = expect(pending).rejects.toThrow('Invalid news filters.');
    http
      .expectOne('/api/news?period=bad')
      .flush({ detail: 'Invalid news filters.' }, { status: 400, statusText: 'Bad Request' });
    await assertion;
  });
});
