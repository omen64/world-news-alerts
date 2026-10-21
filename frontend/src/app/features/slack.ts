import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api, PageState } from '../core/api';
interface Connection {
  id: string;
  slackUserId: string;
  teamName: string;
  isActive: boolean;
}
@Component({
  imports: [FormsModule, RouterLink],
  template: `<div class="page-heading">
      <div>
        <p class="eyebrow">KEEP YOUR TEAM IN THE KNOW</p>
        <h1>Slack workspaces<span class="accent">.</span></h1>
        <p class="subtitle">Bring world news to a shared channel or your direct messages.</p>
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
        <p class="step">THE PUBLIC WORKSPACE</p>
        <h2>A shared view of the world</h2>
        <p>
          Join category channels in the World News Alerts workspace. Each channel has one common
          feed for all members.
        </p>
        @if (api.catalog()?.publicSlackInviteUrl) {
          <a
            class="button secondary"
            [href]="api.catalog()?.publicSlackInviteUrl"
            target="_blank"
            rel="noopener noreferrer"
            >Join public workspace ↗</a
          >
        } @else {
          <p class="notice">The administrator has not configured a public invitation link.</p>
        }
      </section>
      <section class="panel">
        <p class="step">YOUR WORKSPACES</p>
        <h2>Make it personal</h2>
        <p>
          Connect your workspace. Direct messages are linked to the Slack member who completes
          authorization.
        </p>
        <button
          class="button"
          [disabled]="busy() || !api.catalog()?.slackConfigured"
          (click)="connect()"
        >
          Connect Slack workspace ↗
        </button>
        @if (!api.catalog()?.slackConfigured) {
          <p class="muted">Slack OAuth is awaiting administrator configuration.</p>
        }
      </section>
    </div>
    @for (connection of connections(); track connection.id) {
      <section class="panel connection">
        <div class="section-title">
          <h2>＃ {{ connection.teamName }}</h2>
          <button
            class="text-button danger"
            [disabled]="busy()"
            (click)="disconnect(connection.id)"
          >
            Disconnect
          </button>
        </div>
        <p class="muted">
          Linked Slack member: {{ connection.slackUserId }}. Your direct-message destination is
          ready in subscription preferences.
        </p>
        <button
          class="button secondary small"
          [disabled]="busy()"
          (click)="channels(connection.id)"
        >
          Choose a channel
        </button>
        @if (activeConnection === connection.id) {
          <form class="inline-form" (ngSubmit)="add()">
            <label
              >Bot-authorized channels<select name="channel" [(ngModel)]="selectedChannel" required>
                <option value="">Select a channel</option>
                @for (channel of available(); track channel.id) {
                  <option [value]="channel.id">#{{ channel.name }}</option>
                }
              </select></label
            ><button class="button small" [disabled]="busy() || !selectedChannel">
              Add destination
            </button>
          </form>
          <p class="muted small-text">
            Invite the bot to a channel first. Only channels the bot has joined appear here.
          </p>
        }
      </section>
    }
    <p>
      <a class="button secondary" routerLink="/subscriptions">Configure delivery preferences →</a>
    </p>`,
})
export class SlackPage extends PageState implements OnInit {
  api = inject(Api);
  route = inject(ActivatedRoute);
  connections = signal<Connection[]>([]);
  available = signal<{ id: string; name: string }[]>([]);
  activeConnection = '';
  selectedChannel = '';
  ngOnInit() {
    void this.perform(async () => {
      await this.load();
      if (this.route.snapshot.queryParamMap.has('connected'))
        this.message.set('Slack connected. Choose a destination in subscription preferences.');
      if (this.route.snapshot.queryParamMap.has('error'))
        this.error.set('Slack authorization was cancelled. You can try again.');
    });
  }
  async load() {
    this.connections.set(await this.api.get<Connection[]>('/slack/connections'));
  }
  async connect() {
    await this.perform(async () => {
      const response = await this.api.send<{ url: string }>('POST', '/slack/connect');
      window.location.assign(response.url);
    });
  }
  async disconnect(id: string) {
    await this.perform(async () => {
      await this.api.send('DELETE', '/slack/connections/' + id);
      await this.load();
      this.message.set('Workspace disconnected from your account.');
    });
  }
  async channels(id: string) {
    await this.perform(async () => {
      this.available.set(
        await this.api.get<{ id: string; name: string }[]>(
          '/slack/connections/' + id + '/channels',
        ),
      );
      this.activeConnection = id;
    });
  }
  async add() {
    await this.perform(async () => {
      await this.api.send('POST', '/slack/connections/' + this.activeConnection + '/destinations', {
        channelId: this.selectedChannel,
      });
      this.message.set('Destination added. Enable it in your subscription preferences.');
    });
  }
}
