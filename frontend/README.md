# World News Alerts frontend

Angular standalone components, lazy routes, Signals and reactive account/subscription forms.

Run `npm ci` then `npm start` here, with the backend listening on port 5080. The development proxy forwards `/api/**` to the backend so authentication cookies and CSRF protection use the same origin.

- `npm test`: Angular/Vitest unit tests.
- `npm run build`: production build in `dist/frontend/browser`.
- `npm run test:e2e`: Playwright reader flow against the running development servers; defaults to installed Microsoft Edge.

See the [root README](../README.md) for backend setup, administrator provisioning, Resend/SMTP configuration, Slack OAuth, Codex retrieval and deployment.
