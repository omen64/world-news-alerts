import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, PageState } from '../core/api';
import { Destination, Subscription } from '../core/models';

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  template: `<div class="page-heading">
      <div>
        <p class="eyebrow">A BRIEFING THAT FITS YOU</p>
        <h1>Your subscriptions<span class="accent">.</span></h1>
        <p class="subtitle">Choose the news you follow and where it reaches you.</p>
      </div>
    </div>
    @if (error()) {
      <div class="error" role="alert">{{ error() }}</div>
    }
    @if (message()) {
      <div class="success" role="status">{{ message() }}</div>
    }
    <form [formGroup]="form" (ngSubmit)="save()" class="settings-grid">
      <section class="panel">
        <p class="step">01 / YOUR INTERESTS</p>
        <h2>What matters to you?</h2>
        <p class="muted">
          Select one or more categories. Matching stories arrive once per destination.
        </p>
        <div class="category-grid">
          @for (category of api.catalog()?.categories; track category.id) {
            <label class="category-option" [class.chosen]="selected().includes(category.id)"
              ><input
                type="checkbox"
                [checked]="selected().includes(category.id)"
                (change)="toggleCategory(category.id)"
              />{{ category.name }}</label
            >
          }
        </div>
        <label
          >Minimum importance<select formControlName="minimumImportance">
            <option value="significant">All significant events</option>
            <option value="exceptional">Exceptional events only</option>
          </select></label
        >
        <label
          >Initial digest period<select formControlName="initialDigestLookbackHours">
            <option [ngValue]="6">Last 6 hours</option>
            <option [ngValue]="24">Last 24 hours</option>
            <option [ngValue]="72">Last 3 days</option>
            <option [ngValue]="168">Last 7 days</option>
          </select></label
        >
        <p class="muted small-text">
          This period applies to your first digest. New qualifying events are delivered as soon as
          they are retrieved.
        </p>
        <label class="check-row"
          ><input type="checkbox" formControlName="notifyUpdates" />Also notify me about meaningful
          story updates</label
        >
      </section>
      <section class="panel">
        <p class="step">02 / YOUR CHANNELS</p>
        <h2>Where should we send it?</h2>
        <label class="delivery-option"
          ><input type="checkbox" formControlName="emailEnabled" /><span
            ><strong>Email alerts</strong><small>{{ api.user()?.email }}</small></span
          ><span class="badge">Email</span></label
        >
        @for (destination of destinations(); track destination.id) {
          @if (destination.channelKey === 'slack' && destination.isActive) {
            <label class="delivery-option"
              ><input
                type="checkbox"
                [checked]="slackIds().includes(destination.id)"
                (change)="toggleSlack(destination.id)"
              /><span
                ><strong>{{ destination.label }}</strong
                ><small>{{
                  destination.kind === 'public_slack'
                    ? 'Shared category feed; channel filters apply'
                    : 'Personal subscription preferences'
                }}</small></span
              ></label
            >
          }
        }
        <p><a routerLink="/integrations/slack">Connect or manage Slack ↗</a></p>
        <hr />
        <p class="step">03 / READY WHEN YOU ARE</p>
        <label class="check-row"
          ><input type="checkbox" formControlName="isActive" />Subscription is active</label
        >
        <p class="muted">
          Your first subscription includes a confirmation email and a separate digest of matching
          stored stories.
        </p>
        <button class="button wide" [disabled]="busy()" type="submit">
          {{ busy() ? 'Saving…' : 'Save my preferences →' }}
        </button>
        @if (subscriptionId) {
          <button type="button" class="text-button danger" [disabled]="busy()" (click)="stop()">
            Unsubscribe from all alerts
          </button>
        }
      </section>
    </form>`,
})
export class SubscriptionsPage extends PageState implements OnInit {
  api = inject(Api);
  fb = inject(FormBuilder);
  selected = signal<string[]>([]);
  slackIds = signal<string[]>([]);
  destinations = signal<Destination[]>([]);
  subscriptionId = '';
  form = this.fb.nonNullable.group({
    minimumImportance: 'significant',
    emailEnabled: true,
    initialDigestLookbackHours: 24,
    isActive: true,
    notifyUpdates: false,
  });
  ngOnInit() {
    void this.perform(async () => {
      const [subscription, destinations] = await Promise.all([
        this.api.get<Subscription | null>('/subscriptions'),
        this.api.get<Destination[]>('/notification-destinations'),
      ]);
      this.destinations.set(destinations);
      if (subscription) {
        this.form.patchValue(subscription);
        this.selected.set(subscription.categories);
        this.slackIds.set(subscription.slackDestinationIds);
        this.subscriptionId = subscription.id!;
      } else {
        this.selected.set(this.api.catalog()?.categories.map((c) => c.id) ?? ['breaking_news']);
      }
    });
  }
  toggleCategory(id: string) {
    this.selected.update((items) =>
      items.includes(id) ? items.filter((i) => i !== id) : [...items, id],
    );
  }
  toggleSlack(id: string) {
    this.slackIds.update((items) =>
      items.includes(id) ? items.filter((i) => i !== id) : [...items, id],
    );
  }
  async save() {
    await this.perform(async () => {
      const value = await this.api.send<Subscription>('PUT', '/subscriptions', {
        ...this.form.getRawValue(),
        categories: this.selected(),
        slackDestinationIds: this.slackIds(),
      });
      this.subscriptionId = value.id!;
      this.message.set('Preferences saved. Your alerts will follow these settings.');
    });
  }
  async stop() {
    await this.perform(async () => {
      await this.api.send('DELETE', '/subscriptions/' + this.subscriptionId);
      this.form.patchValue({ isActive: false });
      this.message.set('All notifications have been stopped.');
    });
  }
}
