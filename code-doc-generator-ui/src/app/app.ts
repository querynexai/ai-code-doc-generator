import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <nav class="top-nav">
      <div class="brand">
        <span class="icon">&lt;/&gt;</span> Query<span class="accent">Nex</span> Docs
      </div>
      <div class="links">
        <a routerLink="/projects" routerLinkActive="active">Projects</a>
      </div>
    </nav>
    <router-outlet />
  `,
  styles: [`
    :host {
      display: block;
      min-height: 100vh;
      background: #0f0f1a;
      color: #e0e0e0;
      font-family: 'Inter', sans-serif;
    }
    .top-nav {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 32px;
      background: #1a1a2e;
      border-bottom: 1px solid #2a2a40;
    }
    .brand { font-size: 20px; font-weight: 800; color: #fff; }
    .icon { color: #00b4d8; margin-right: 6px; }
    .accent { color: #00b4d8; }
    .links { display: flex; gap: 20px; }
    .links a {
      color: #ccc;
      text-decoration: none;
      font-weight: 500;
      padding: 6px 12px;
      border-radius: 6px;
      transition: all 0.2s;
    }
    .links a:hover { color: #00b4d8; }
    .links a.active { background: rgba(0, 180, 216, 0.15); color: #00b4d8; }
  `]
})
export class App {}