import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api, PageState } from '../core/api';
@Component({
  imports: [FormsModule],
  template: `<div class="page-heading">
      <div>
        <p class="eyebrow">MAKE YOURSELF AT HOME</p>
        <h1>Account settings<span class="accent">.</span></h1>
      </div>
    </div>
    @if (error()) {
      <div class="error" role="alert">{{ error() }}</div>
    }
    @if (message()) {
      <div class="success" role="status">{{ message() }}</div>
    }
    <div class="settings-grid">
      <section class="panel">
        <h2>Your profile</h2>
        <form (ngSubmit)="save()">
          <label
            >Display name<input
              name="name"
              [(ngModel)]="displayName"
              required
              maxlength="100" /></label
          ><label>Email<input [value]="api.user()?.email" disabled /></label>
          <p class="muted small-text">
            Your verified email is used for account access and email delivery.
          </p>
          <button class="button" [disabled]="busy()">Save profile →</button>
        </form>
      </section>
      <section class="panel">
        <h2>Change password</h2>
        <form (ngSubmit)="changePassword()">
          <label
            >Current password<input
              type="password"
              name="current"
              [(ngModel)]="currentPassword"
              required
              autocomplete="current-password" /></label
          ><label
            >New password<input
              type="password"
              name="new"
              [(ngModel)]="newPassword"
              required
              minlength="10"
              autocomplete="new-password" /></label
          ><button class="button secondary" [disabled]="busy()">Update password</button>
        </form>
      </section>
      <section class="panel danger-panel">
        <h2>Delete account</h2>
        <p>This permanently removes your account, subscription and private delivery history.</p>
        <form (ngSubmit)="remove()">
          <label
            >Confirm your password<input
              name="deletePassword"
              type="password"
              [(ngModel)]="deletePassword"
              required
              autocomplete="current-password" /></label
          ><label class="check-row"
            ><input type="checkbox" name="confirmDelete" [(ngModel)]="confirmDelete" />I understand
            this cannot be undone.</label
          ><button class="button danger-button" [disabled]="busy() || !confirmDelete">
            Delete my account
          </button>
        </form>
      </section>
    </div>`,
})
export class SettingsPage extends PageState {
  api = inject(Api);
  router = inject(Router);
  displayName = this.api.user()?.displayName ?? '';
  currentPassword = '';
  newPassword = '';
  deletePassword = '';
  confirmDelete = false;
  async save() {
    await this.perform(async () => {
      await this.api.send('PUT', '/profile', { displayName: this.displayName });
      await this.api.session();
      this.message.set('Profile updated.');
    });
  }
  async changePassword() {
    await this.perform(async () => {
      await this.api.send('POST', '/profile/change-password', {
        currentPassword: this.currentPassword,
        newPassword: this.newPassword,
      });
      this.currentPassword = '';
      this.newPassword = '';
      this.message.set('Password updated.');
    });
  }
  async remove() {
    if (!this.confirmDelete) return;
    await this.perform(async () => {
      await this.api.send('DELETE', '/profile', { password: this.deletePassword });
      this.api.user.set(null);
      await this.router.navigateByUrl('/register');
    });
  }
}
