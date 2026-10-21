import { Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api, PageState } from '../core/api';

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  template: `<div class="auth-layout">
    <div class="auth-intro">
      <p class="eyebrow">A WIDER PERSPECTIVE</p>
      <h1>Your world.<br />Your briefing.</h1>
      <p>
        Follow the categories that matter to you.<br />Receive important developments by email and
        Slack.
      </p>
      <div class="orbit" aria-hidden="true">◎</div>
    </div>
    <section class="panel auth-form">
      <p class="eyebrow">WORLD NEWS ALERTS</p>
      <h2>{{ title }}</h2>
      @if (error()) {
        <div class="error" role="alert">{{ error() }}</div>
      }
      @if (message()) {
        <div class="success" role="status">{{ message() }}</div>
      }
      @if (developmentUrl) {
        <div class="notice">
          Development inbox: <a [href]="developmentUrl">Open email verification link ↗</a>
        </div>
      }
      <form [formGroup]="form" (ngSubmit)="submit()">
        @if (mode === 'register') {
          <label
            >Display name<input
              formControlName="displayName"
              autocomplete="name"
              maxlength="100"
              required
          /></label>
        }
        @if (['login', 'register', 'forgot-password', 'reset-password'].includes(mode)) {
          <label
            >Email address<input type="email" formControlName="email" autocomplete="email" required
          /></label>
        }
        @if (['login', 'register', 'reset-password'].includes(mode)) {
          <label
            >Password<input
              type="password"
              formControlName="password"
              [autocomplete]="mode === 'login' ? 'current-password' : 'new-password'"
              required
              [minLength]="mode === 'login' ? 1 : 10"
          /></label>
          @if (mode !== 'login') {
            <small class="muted"
              >At least 10 characters, with uppercase, lowercase, a number and a symbol.</small
            >
          }
        }
        @if (mode === 'verify-email') {
          <p>Confirm this address to activate your account and news alerts.</p>
        }
        @if (mode === 'unsubscribe') {
          <p>Stop all news notifications. You can reactivate your preferences after signing in.</p>
        }
        <button class="button wide" type="submit" [disabled]="busy() || completed">
          {{ busy() ? 'Please wait…' : button }}
        </button>
      </form>
      @if (mode === 'login') {
        <p><a routerLink="/forgot-password">Forgot password?</a></p>
        <p>New here? <a routerLink="/register">Create an account ↗</a></p>
        <button class="text-button" [disabled]="busy()" (click)="resend()">
          Resend verification email
        </button>
      } @else {
        <p><a routerLink="/login">Back to sign in →</a></p>
      }
      @if (api.catalog()?.developmentEmail) {
        <p class="muted small-text">
          Development mode saves emails in the backend's App_Data/mail directory.
        </p>
      }
    </section>
  </div>`,
})
export class AuthPage extends PageState {
  api = inject(Api);
  route = inject(ActivatedRoute);
  router = inject(Router);
  fb = inject(FormBuilder);
  mode = this.route.snapshot.url[0]?.path ?? 'login';
  developmentUrl = '';
  completed = false;
  form = this.fb.nonNullable.group({ email: [''], password: [''], displayName: [''] });
  get title() {
    return (
      {
        login: 'Welcome back',
        register: 'Create your account',
        'verify-email': 'Verify your email',
        'forgot-password': 'Reset your password',
        'reset-password': 'Choose a new password',
        unsubscribe: 'Unsubscribe from alerts',
      } as Record<string, string>
    )[this.mode];
  }
  get button() {
    return (
      {
        login: 'Sign in →',
        register: 'Create account →',
        'verify-email': 'Verify email →',
        'forgot-password': 'Send reset link →',
        'reset-password': 'Save password →',
        unsubscribe: 'Stop all notifications',
      } as Record<string, string>
    )[this.mode];
  }
  constructor() {
    super();
    const email = this.route.snapshot.queryParamMap.get('email');
    if (email) this.form.patchValue({ email });
  }
  async submit() {
    await this.perform(async () => {
      const value = this.form.getRawValue();
      const query = this.route.snapshot.queryParamMap;
      if (this.mode === 'login') {
        await this.api.send('POST', '/auth/login', value);
        await this.api.session();
        await this.router.navigateByUrl('/news');
        return;
      }
      let path = '/auth/' + this.mode,
        body: unknown = value;
      if (this.mode === 'verify-email')
        body = { userId: query.get('userId'), token: query.get('token') };
      if (this.mode === 'reset-password') body = { ...value, token: query.get('token') };
      if (this.mode === 'unsubscribe') {
        path = '/unsubscribe';
        body = { token: query.get('token') };
      }
      const result = await this.api.send<{ message: string; developmentVerificationUrl?: string }>(
        'POST',
        path,
        body,
      );
      this.message.set(result.message);
      this.developmentUrl = result.developmentVerificationUrl ?? '';
      this.completed = ['verify-email', 'unsubscribe', 'register', 'reset-password'].includes(
        this.mode,
      );
    });
  }
  async resend() {
    await this.perform(async () => {
      const result = await this.api.send<{ message: string }>('POST', '/auth/resend-verification', {
        email: this.form.getRawValue().email,
      });
      this.message.set(result.message);
    });
  }
}
