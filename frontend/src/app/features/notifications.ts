import { Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Api, PageState } from '../core/api';
import { Delivery, Page } from '../core/models';
@Component({
  imports: [DatePipe],
  template: `<div class="page-heading">
      <div>
        <p class="eyebrow">FROM OUR DESK TO YOU</p>
        <h1>Delivery history<span class="accent">.</span></h1>
        <p class="subtitle">Track your emails and Slack alerts.</p>
      </div>
      <button class="button secondary" [disabled]="busy()" (click)="load()">Refresh ↻</button>
    </div>
    @if (error()) {
      <div class="error" role="alert">{{ error() }}</div>
    }
    <section class="panel table-panel">
      <table>
        <thead>
          <tr>
            <th>Message</th>
            <th>Channel</th>
            <th>Status</th>
            <th>Time / UTC</th>
          </tr>
        </thead>
        <tbody>
          @for (item of result().items; track item.id) {
            <tr>
              <td>
                <strong>{{ item.subject }}</strong
                ><small>{{ item.messageType }} · {{ item.attemptCount }} attempt(s)</small>
                @if (item.error) {
                  <small class="danger">{{ item.error }}</small>
                }
              </td>
              <td>{{ item.channel }}</td>
              <td>
                <span
                  class="badge"
                  [class.sent]="item.status === 'Sent'"
                  [class.failed]="item.status === 'Failed'"
                  >{{ item.status }}</span
                >
              </td>
              <td>
                {{ item.createdAtUtc | date: 'dd MMM, HH:mm' : 'UTC' }}
                @if (item.status === 'RetryScheduled') {
                  <small>Retry: {{ item.nextAttemptAtUtc | date: 'HH:mm' : 'UTC' }} UTC</small>
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="4" class="empty">
                No deliveries yet. Create a subscription to receive your first briefing.
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
        [disabled]="page * 20 >= result().total || busy()"
        (click)="page = page + 1; load()"
      >
        Next →
      </button>
    </div>`,
})
export class NotificationsPage extends PageState implements OnInit {
  api = inject(Api);
  page = 1;
  result = signal<Page<Delivery>>({ items: [], total: 0, page: 1, pageSize: 20 });
  ngOnInit() {
    void this.load();
  }
  async load() {
    await this.perform(async () => {
      this.result.set(await this.api.get<Page<Delivery>>('/notifications?page=' + this.page));
    });
  }
}
