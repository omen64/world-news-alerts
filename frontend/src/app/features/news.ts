import { Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api, PageState } from '../core/api';
import { News, Page } from '../core/models';

@Component({
  imports: [DatePipe, FormsModule, RouterLink],
  template: ` <div class="page-heading">
      <div>
        <p class="eyebrow">YOUR GLOBAL BRIEFING</p>
        <h1>The world feed<span class="accent">.</span></h1>
        <p class="subtitle">The developments that matter, all in one place.</p>
      </div>
      <div class="date-stamp">
        {{ today | date: 'EEEE' }}<strong>{{ today | date: 'dd MMM yyyy' }}</strong>
      </div>
    </div>
    @if (api.catalog()?.newsProvider === 'fake') {
      <div class="notice">DEMO EDITION · The stories below are fictional development fixtures.</div>
    }
    <div class="feed-layout">
      <aside class="filter-panel panel">
        <div class="section-title">
          Refine your feed<button class="text-button" (click)="reset()">Reset</button>
        </div>
        <fieldset>
          <legend>Categories</legend>
          @for (category of api.catalog()?.categories; track category.id) {
            <label class="check-row"
              ><input
                type="checkbox"
                [checked]="categories.includes(category.id)"
                (change)="toggle(category.id)"
              />{{ category.name }}</label
            >
          }
        </fieldset>
        <fieldset>
          <legend>Time period</legend>
          <div class="segments">
            @for (option of periods; track option.key) {
              <button
                [class.selected]="period === option.key"
                [attr.aria-pressed]="period === option.key"
                (click)="period = option.key; load()"
              >
                {{ option.label }}
              </button>
            }
          </div>
        </fieldset>
        <fieldset>
          <legend>Importance</legend>
          <label class="check-row"
            ><input
              type="radio"
              name="importance"
              value="significant"
              [(ngModel)]="importance"
              (change)="load()"
            />All significant events</label
          ><label class="check-row"
            ><input
              type="radio"
              name="importance"
              value="exceptional"
              [(ngModel)]="importance"
              (change)="load()"
            />Exceptional only</label
          >
        </fieldset>
        <div class="filter-note">
          Make it your briefing.
          <p>Get matching stories by email or Slack.</p>
          <a routerLink="/subscriptions">Set up alerts ↗</a>
        </div>
      </aside>
      <section aria-label="News results">
        <form class="searchbar" (ngSubmit)="load()">
          <span aria-hidden="true">⌕</span
          ><input
            aria-label="Search news"
            name="search"
            [(ngModel)]="search"
            placeholder="Search world events…"
          /><button class="button secondary small" type="submit">Search</button>
        </form>
        <div class="results-label">
          <span>{{ result().total }} DEVELOPMENTS</span><span>Most recent first · UTC</span>
        </div>
        @if (error()) {
          <div class="error" role="alert">
            {{ error() }} <button (click)="load()">Retry</button>
          </div>
        }
        @if (busy()) {
          <div class="empty" role="status">Loading your briefing…</div>
        } @else {
          @for (item of result().items; track item.id) {
            <article class="news-card">
              <div class="news-meta">
                <span class="category-label">{{ name(item.categories[0]) }}</span
                ><span class="badge" [class.exceptional]="item.importance === 'exceptional'">{{
                  item.importance
                }}</span>
              </div>
              <h2>
                <a [routerLink]="['/news', item.id]">{{ item.title }}</a>
              </h2>
              <p>{{ item.summary }}</p>
              <div class="story-footer">
                <span
                  >{{ item.location || 'Worldwide' }} <span class="muted">·</span>
                  {{ item.effectiveAtUtc | date: 'dd MMM, HH:mm' : 'UTC' }} UTC</span
                ><span
                  >{{ item.sources.length }} source{{ item.sources.length === 1 ? '' : 's' }}
                  <a [routerLink]="['/news', item.id]" aria-label="Read event details">↗</a></span
                >
              </div>
            </article>
          } @empty {
            <div class="empty">
              <h2>A quieter view of the world.</h2>
              <p>No events match these filters. Try another category or a longer period.</p>
              <button class="button secondary" (click)="reset()">Reset filters</button>
            </div>
          }
        }
        <div class="pagination">
          <button
            class="button secondary small"
            [disabled]="page <= 1 || busy()"
            (click)="page = page - 1; load(false)"
          >
            ← Previous</button
          ><span>Page {{ page }} of {{ pages() }}</span
          ><button
            class="button secondary small"
            [disabled]="page * 20 >= result().total || busy()"
            (click)="page = page + 1; load(false)"
          >
            Next →
          </button>
        </div>
      </section>
    </div>`,
})
export class NewsPage extends PageState implements OnInit {
  api = inject(Api);
  today = new Date();
  categories: string[] = [];
  period = '24h';
  importance = 'significant';
  search = '';
  page = 1;
  periods = [
    { key: '6h', label: '6h' },
    { key: '24h', label: '24h' },
    { key: '3d', label: '3 days' },
    { key: '7d', label: '7 days' },
  ];
  result = signal<Page<News>>({ items: [], total: 0, page: 1, pageSize: 20 });
  private loadSequence = 0;
  ngOnInit() {
    void this.load();
  }
  name(id: string) {
    return this.api.catalog()?.categories.find((c) => c.id === id)?.name ?? id;
  }
  pages() {
    return Math.max(1, Math.ceil(this.result().total / 20));
  }
  toggle(id: string) {
    this.categories = this.categories.includes(id)
      ? this.categories.filter((c) => c !== id)
      : [...this.categories, id];
    void this.load();
  }
  reset() {
    this.categories = [];
    this.period = '24h';
    this.importance = 'significant';
    this.search = '';
    void this.load();
  }
  async load(resetPage = true) {
    if (resetPage) this.page = 1;
    const sequence = ++this.loadSequence;
    this.busy.set(true);
    this.error.set('');
    const params = new URLSearchParams({
      categories: this.categories.join(','),
      period: this.period,
      importance: this.importance,
      search: this.search,
      page: String(this.page),
      pageSize: '20',
    });
    try {
      const result = await this.api.get<Page<News>>('/news?' + params);
      if (sequence === this.loadSequence) this.result.set(result);
    } catch (error) {
      if (sequence === this.loadSequence)
        this.error.set(error instanceof Error ? error.message : 'Unable to load news.');
    } finally {
      if (sequence === this.loadSequence) this.busy.set(false);
    }
  }
}
@Component({
  imports: [DatePipe, RouterLink],
  template: `<a class="back-link" routerLink="/news">← Back to world feed</a>
    @if (error()) {
      <div class="error" role="alert">{{ error() }}</div>
    }
    @if (item(); as item) {
      <article class="detail panel">
        <p class="eyebrow">{{ item.categories.join(' · ').replaceAll('_', ' ') }}</p>
        <span class="badge exceptional">{{ item.importance }}</span>
        <h1>{{ item.title }}</h1>
        <p class="lead">{{ item.summary }}</p>
        <div class="metadata-grid">
          <div>
            Location<strong>{{ item.location || 'Not specified' }}</strong>
          </div>
          <div>
            Event time<strong>{{
              item.eventTimeUtc ? (item.eventTimeUtc | date: 'medium' : 'UTC') + ' UTC' : 'Unknown'
            }}</strong>
          </div>
          <div>
            Retrieved<strong>{{ item.retrievedAtUtc | date: 'medium' : 'UTC' }} UTC</strong>
          </div>
          <div>
            Revision<strong>{{ item.revision }}</strong>
          </div>
        </div>
        <h2>Read the sources</h2>
        <p class="muted">Review the original reporting for the full context.</p>
        @for (source of item.sources; track source.url) {
          <a class="source-row" [href]="source.url" target="_blank" rel="noopener noreferrer"
            ><span
              ><strong>{{ source.publisher }}</strong
              ><small>{{
                source.publishedAtUtc
                  ? (source.publishedAtUtc | date: 'medium' : 'UTC') + ' UTC'
                  : 'Publication time unknown'
              }}</small></span
            ><span>Open source ↗</span></a
          >
        }
        <p><a class="button" routerLink="/subscriptions">Get alerts like this ↗</a></p>
      </article>
    } @else if (busy()) {
      <div class="empty">Loading event…</div>
    }`,
})
export class NewsDetailPage extends PageState implements OnInit {
  private api = inject(Api);
  private route = inject(ActivatedRoute);
  item = signal<News | null>(null);
  ngOnInit() {
    void this.perform(async () => {
      this.item.set(await this.api.get<News>('/news/' + this.route.snapshot.paramMap.get('id')));
    });
  }
}
