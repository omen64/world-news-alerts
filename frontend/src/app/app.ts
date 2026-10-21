import { Component, inject, OnInit } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api } from './core/api';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: ` <a class="skip-link" href="#main">Skip to content</a>
    <aside class="sidebar">
      <a class="brand" routerLink="/news"
        ><span class="brand-mark">◎</span><span>WORLD NEWS<small>ALERTS</small></span></a
      >
      <p class="nav-label">YOUR BRIEFING</p>
      <nav aria-label="Main navigation">
        <a routerLink="/news" routerLinkActive="active"><span>◉</span> World feed</a>
        <a routerLink="/subscriptions" routerLinkActive="active"><span>◈</span> Subscriptions</a>
        <a routerLink="/notifications" routerLinkActive="active"><span>↗</span> Delivery history</a>
        <a routerLink="/integrations/slack" routerLinkActive="active"
          ><span>＃</span> Slack workspaces</a
        >
        <a routerLink="/settings" routerLinkActive="active"><span>⚙</span> Account settings</a>
        @if (api.user()?.isAdmin) {
          <a routerLink="/admin" routerLinkActive="active"><span>▦</span> Administration</a>
        }
      </nav>
      <div class="sidebar-bottom">
        <span class="status-dot"></span> A wider perspective.
        <p>Important world events.<br />Your categories. Your channels.</p>
      </div>
    </aside>
    <div class="workspace">
      <header class="topbar">
        <span class="edition">THE WORLD, IN PERSPECTIVE</span>
        <div class="account-actions">
          @if (api.user(); as user) {
            <span class="avatar">{{ user.displayName.slice(0, 1).toUpperCase() }}</span
            ><span>{{ user.displayName }}</span
            ><button class="text-button" (click)="logout()">Sign out</button>
          } @else {
            <a routerLink="/login">Sign in</a
            ><a class="button small" routerLink="/register">Create account ↗</a>
          }
        </div>
      </header>
      <main id="main"><router-outlet /></main>
      <footer>WORLD NEWS ALERTS <span>Stay informed. Choose what matters.</span></footer>
    </div>`,
})
export class App implements OnInit {
  api = inject(Api);
  private router = inject(Router);
  async ngOnInit() {
    await Promise.all([this.api.session(), this.api.loadCatalog().catch(() => {})]);
  }
  async logout() {
    try {
      await this.api.logout();
      await this.router.navigateByUrl('/news');
    } catch {
      await this.router.navigateByUrl('/login');
    }
  }
}
