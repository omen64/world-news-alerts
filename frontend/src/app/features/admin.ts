import { Component, inject, OnInit, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink, RouterLinkActive } from '@angular/router';
import { Api, PageState } from '../core/api';
import { Delivery, Page, RuntimeSettings } from '../core/models';
interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  emailConfirmed: boolean;
  isDisabled: boolean;
  roles: string[];
  categories: string[];
  destinations: string[];
}
interface Execution {
  id: string;
  providerKey: string;
  startedAtUtc: string;
  completedAtUtc: string | null;
  status: string;
  eventsCount: number;
  insertedCount: number;
  error: string | null;
}
interface Jobs {
  executions: Execution[];
  deliveries: Delivery[];
  deliveryCounts: { status: string; count: number }[];
}
interface Channels {
  emailProviders: { key: string; name: string; configured: boolean }[];
  slackConfigured: boolean;
}
@Component({
  imports: [DatePipe, FormsModule, RouterLink, RouterLinkActive],
  template: ` <div class="page-heading">
      <div>
        <p class="eyebrow">OPERATIONS DESK</p>
        <h1>Administration<span class="accent">.</span></h1>
        <p class="subtitle">Manage providers, channels, subscribers and delivery jobs.</p>
      </div>
      <button class="button secondary" [disabled]="busy()" (click)="load()">Refresh ↻</button>
    </div>
    <nav class="tabs" aria-label="Administration">
      <a routerLink="/admin" [routerLinkActiveOptions]="{ exact: true }" routerLinkActive="active"
        >Overview</a
      ><a routerLink="/admin/users" routerLinkActive="active">Users</a
      ><a routerLink="/admin/providers" routerLinkActive="active">News providers</a
      ><a routerLink="/admin/channels" routerLinkActive="active">Delivery channels</a
      ><a routerLink="/admin/jobs" routerLinkActive="active">Jobs & retries</a>
    </nav>
    @if (error()) {
      <div class="error" role="alert">{{ error() }}</div>
    }
    @if (message()) {
      <div class="success" role="status">{{ message() }}</div>
    }
    @if (section() === 'overview') {
      <div class="stat-grid">
        @for (count of jobs()?.deliveryCounts; track count.status) {
          <div class="panel stat">
            <span>{{ count.status }}</span
            ><strong>{{ count.count }}</strong
            ><small>delivery jobs</small>
          </div>
        }
        <div class="panel stat">
          <span>Retrieval provider</span><strong>{{ settings()?.newsProvider }}</strong
          ><small>Every {{ settings()?.retrievalIntervalMinutes }} minutes</small>
        </div>
        <div class="panel stat">
          <span>Email provider</span><strong>{{ settings()?.emailProvider }}</strong
          ><small>Immediate outbox dispatch</small>
        </div>
      </div>
      <section class="panel">
        <h2>Delivery controls</h2>
        <p>Retrieve news on demand or process eligible pending messages.</p>
        <div class="button-row">
          <button class="button" [disabled]="busy()" (click)="retrieve()">
            Retrieve news now ↗</button
          ><button class="button secondary" [disabled]="busy()" (click)="dispatch()">
            Process outbox
          </button>
        </div>
      </section>
    }
    @if (section() === 'users') {
      <section class="panel table-panel">
        <table>
          <thead>
            <tr>
              <th>User</th>
              <th>Account</th>
              <th>Subscription</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (user of users().items; track user.id) {
              <tr>
                <td>
                  <strong>{{ user.displayName }}</strong
                  ><small>{{ user.email }}</small
                  ><small>{{ user.roles.join(', ') }}</small>
                </td>
                <td>
                  {{ user.emailConfirmed ? 'Verified' : 'Unverified'
                  }}<small>{{ user.isDisabled ? 'Disabled' : 'Enabled' }}</small>
                </td>
                <td>
                  {{ user.categories.join(', ').replaceAll('_', ' ') || 'No categories'
                  }}<small>{{ user.destinations.join(', ') }}</small>
                </td>
                <td>
                  <button
                    class="button secondary small"
                    [disabled]="busy() || user.id === api.user()?.id"
                    (click)="disable(user)"
                  >
                    {{ user.isDisabled ? 'Enable' : 'Disable' }}
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
      <div class="pagination">
        <button
          class="button secondary small"
          [disabled]="page <= 1 || busy()"
          (click)="page = page - 1; load()"
        >
          ← Previous</button
        ><span>Page {{ page }}</span
        ><button
          class="button secondary small"
          [disabled]="page * 50 >= users().total || busy()"
          (click)="page = page + 1; load()"
        >
          Next →
        </button>
      </div>
    }
    @if (settings(); as settings) {
      @if (section() === 'providers') {
        <section class="panel narrow">
          <h2>News retrieval</h2>
          <form (ngSubmit)="save()">
            <label
              >Active provider<select name="provider" [(ngModel)]="settings.newsProvider">
                @for (provider of newsProviders(); track provider.key) {
                  <option [value]="provider.key">{{ provider.name }}</option>
                }
              </select></label
            ><label class="check-row"
              ><input
                type="checkbox"
                name="retrievalEnabled"
                [(ngModel)]="settings.retrievalEnabled"
              />Enable scheduled retrieval</label
            >
            <div class="form-columns">
              <label
                >Interval / minutes<input
                  type="number"
                  name="interval"
                  [(ngModel)]="settings.retrievalIntervalMinutes"
                  min="1"
                  max="1440"
                  required /></label
              ><label
                >Overlap lookback / minutes<input
                  type="number"
                  name="lookback"
                  [(ngModel)]="settings.lookbackMinutes"
                  min="1"
                  max="10080"
                  required
              /></label>
            </div>
            <p class="muted">
              Codex requires a native executable, unattended authentication and working live search
              on the backend host.
            </p>
            <button class="button" [disabled]="busy()">Save retrieval settings →</button>
          </form>
          <hr />
          <button class="button secondary" [disabled]="busy()" (click)="retrieve()">
            Retrieve news now
          </button>
        </section>
      }
      @if (section() === 'channels') {
        <div class="settings-grid">
          <section class="panel">
            <h2>Email delivery</h2>
            <form (ngSubmit)="save()">
              <label class="check-row"
                ><input
                  type="checkbox"
                  name="emailEnabled"
                  [(ngModel)]="settings.emailEnabled"
                />Enable email</label
              ><label
                >Email provider<select name="emailProvider" [(ngModel)]="settings.emailProvider">
                  @for (provider of channels()?.emailProviders; track provider.key) {
                    <option [value]="provider.key">{{ provider.name }}</option>
                  }
                </select></label
              >
              @for (provider of channels()?.emailProviders; track provider.key) {
                <div class="configuration-row">
                  <strong>{{ provider.name }}</strong
                  ><span class="badge" [class.sent]="provider.configured">{{
                    provider.configured ? 'Configured' : 'Needs configuration'
                  }}</span>
                </div>
              }
              <p class="muted small-text">
                Credentials and sender addresses are configured on the server. API keys and
                passwords are never displayed here.
              </p>
              <button class="button" [disabled]="busy()">Save channel settings →</button>
            </form>
            <hr />
            <button class="button secondary" [disabled]="busy()" (click)="testEmail()">
              Send test to my email
            </button>
          </section>
          <section class="panel">
            <h2>Dispatch & retries</h2>
            <form (ngSubmit)="save()">
              <label class="check-row"
                ><input
                  type="checkbox"
                  name="notifications"
                  [(ngModel)]="settings.notificationsEnabled"
                />Enable notification dispatch</label
              ><label class="check-row"
                ><input
                  type="checkbox"
                  name="slackEnabled"
                  [(ngModel)]="settings.slackEnabled"
                />Enable Slack delivery</label
              ><label
                >Fallback scan / seconds<input
                  type="number"
                  name="polling"
                  [(ngModel)]="settings.pollingIntervalSeconds"
                  min="1"
                  max="300"
                  required /></label
              ><label
                >Maximum attempts<input
                  type="number"
                  name="retries"
                  [(ngModel)]="settings.maximumRetryCount"
                  min="1"
                  max="10"
                  required /></label
              ><label
                >Initial retry delay / seconds<input
                  type="number"
                  name="delay"
                  [(ngModel)]="settings.initialRetryDelaySeconds"
                  min="1"
                  max="3600"
                  required /></label
              ><button class="button" [disabled]="busy()">Save dispatch settings →</button>
            </form>
          </section>
        </div>
      }
    }
    @if (section() === 'jobs') {
      <div class="button-row">
        <button class="button" [disabled]="busy()" (click)="retrieve()">Retrieve news now ↗</button
        ><button class="button secondary" [disabled]="busy()" (click)="dispatch()">
          Process outbox
        </button>
      </div>
      <h2>Retrieval executions</h2>
      <section class="panel table-panel">
        <table>
          <thead>
            <tr>
              <th>Provider</th>
              <th>Status</th>
              <th>Events / new</th>
              <th>Started / UTC</th>
            </tr>
          </thead>
          <tbody>
            @for (job of jobs()?.executions; track job.id) {
              <tr>
                <td>{{ job.providerKey }}</td>
                <td>
                  <span class="badge" [class.failed]="job.status === 'Failed'">{{
                    job.status
                  }}</span>
                  @if (job.error) {
                    <small>{{ job.error }}</small>
                  }
                </td>
                <td>{{ job.eventsCount }} / {{ job.insertedCount }}</td>
                <td>{{ job.startedAtUtc | date: 'dd MMM, HH:mm:ss' : 'UTC' }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="4">No retrievals yet.</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
      <h2>Delivery jobs</h2>
      <section class="panel table-panel">
        <table>
          <thead>
            <tr>
              <th>Message</th>
              <th>Status</th>
              <th>Attempts</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (job of jobs()?.deliveries; track job.id) {
              <tr>
                <td>
                  {{ job.subject }}
                  @if (job.error) {
                    <small class="danger">{{ job.error }}</small>
                  }
                </td>
                <td>
                  <span
                    class="badge"
                    [class.sent]="job.status === 'Sent'"
                    [class.failed]="job.status === 'Failed'"
                    >{{ job.status }}</span
                  >
                </td>
                <td>{{ job.attemptCount }}</td>
                <td>
                  @if (job.status === 'Failed' || job.status === 'RetryScheduled') {
                    <button
                      class="button secondary small"
                      [disabled]="busy()"
                      (click)="retry(job.id)"
                    >
                      Retry
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
      <div class="pagination">
        <button
          class="button secondary small"
          [disabled]="page <= 1 || busy()"
          (click)="page = page - 1; load()"
        >
          ← Previous</button
        ><span>Page {{ page }}</span
        ><button
          class="button secondary small"
          [disabled]="
            ((jobs()?.deliveries?.length ?? 0) < 30 && (jobs()?.executions?.length ?? 0) < 30) ||
            busy()
          "
          (click)="page = page + 1; load()"
        >
          Next →
        </button>
      </div>
    }`,
})
export class AdminPage extends PageState implements OnInit {
  api = inject(Api);
  route = inject(ActivatedRoute);
  destroy = inject(DestroyRef);
  section = signal('overview');
  settings = signal<RuntimeSettings | null>(null);
  channels = signal<Channels | null>(null);
  jobs = signal<Jobs | null>(null);
  users = signal<Page<AdminUser>>({ items: [], total: 0, page: 1, pageSize: 50 });
  newsProviders = signal<{ key: string; name: string }[]>([]);
  page = 1;
  ngOnInit() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe((params) => {
      this.section.set(params.get('section') ?? 'overview');
      this.page = 1;
      void this.load();
    });
  }
  async refresh() {
    const [settings, channels, jobs, users, providers] = await Promise.all([
      this.api.get<RuntimeSettings>('/admin/settings'),
      this.api.get<Channels>('/admin/channels'),
      this.api.get<Jobs>('/admin/jobs?page=' + this.page),
      this.api.get<Page<AdminUser>>('/admin/users?page=' + this.page),
      this.api.get<{ providers: { key: string; name: string }[] }>('/admin/providers'),
    ]);
    this.settings.set(settings);
    this.channels.set(channels);
    this.jobs.set(jobs);
    this.users.set(users);
    this.newsProviders.set(providers.providers);
  }
  async load() {
    await this.perform(() => this.refresh());
  }
  async save() {
    await this.perform(async () => {
      await this.api.send('PUT', '/admin/settings', this.settings());
      await this.api.loadCatalog();
      this.message.set('Settings saved.');
    });
  }
  async retrieve() {
    await this.perform(async () => {
      const result = await this.api.send<Execution>('POST', '/admin/jobs/retrieve-news');
      await this.refresh();
      if (result.status === 'Failed') throw new Error(result.error ?? 'Retrieval failed.');
      this.message.set(`Retrieval complete: ${result.insertedCount} new events.`);
    });
  }
  async dispatch() {
    await this.perform(async () => {
      await this.api.send('POST', '/admin/jobs/dispatch');
      await this.refresh();
      this.message.set('Eligible delivery jobs processed.');
    });
  }
  async retry(id: string) {
    await this.perform(async () => {
      await this.api.send('POST', '/admin/jobs/' + id + '/retry');
      await this.refresh();
      this.message.set('Delivery queued for retry.');
    });
  }
  async testEmail() {
    await this.perform(async () => {
      const result = await this.api.send<{ message: string }>('POST', '/admin/channels/test-email');
      this.message.set(result.message);
    });
  }
  async disable(user: AdminUser) {
    await this.perform(async () => {
      await this.api.send('PUT', '/admin/users/' + user.id, { isDisabled: !user.isDisabled });
      await this.refresh();
    });
  }
}
